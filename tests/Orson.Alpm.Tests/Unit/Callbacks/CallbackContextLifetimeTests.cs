using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Orson.Alpm.Tests.Fixtures;

namespace Orson.Alpm.Tests.Unit.Callbacks;

/// <summary>
/// The ctx handle is how a native thunk finds its <see cref="AlpmCallbacks" />: libalpm stores one value per
/// registered callback and hands it back on every invocation. These tests verify the lifetime of that value:
/// weak enough that a dropped session is collected, and properly freed when the session is disposed.
/// </summary>
/// <remarks>
/// The slot is freed when <see cref="AlpmSession.Dispose()" /> destroys the handle. Dropping a session without
/// disposing it exercises <see cref="SafeAlpmHandle" />'s critical finalizer, which frees unmanaged resources.
/// </remarks>
[Collection("AlpmSessionSerial")]
public sealed class CallbackContextLifetimeTests
{
  /// <summary>
  /// Dropping an <see cref="AlpmSession" /> without disposing it must not root the <see cref="AlpmCallbacks" />:
  /// the ctx handle is weak, so the handler delegates it keeps alive die with the instance.
  /// </summary>
  [Fact]
  public void AlpmFinalizer_DoesNotRootTheCallback()
  {
    var (callback, workspace) = CreateAlpmWithoutDisposing();

    CollectUntilDead(callback);

    Assert.False(callback.IsAlive,
      "Callback is still rooted after Alpm was finalized although the ctx handle is weak.");

    DeleteWorkspace(workspace);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static (WeakReference Callback, string Workspace) CreateAlpmWithoutDisposing()
  {
    var environment = new IsolatedAlpmEnvironment();
    var weak = new WeakReference(environment.AlpmSession.Callbacks);

    // Intentionally not disposed: this exercises SafeAlpmHandle's critical finalizer.
    return (weak, environment.WorkspaceRoot);
  }

  /// <summary>
  /// Deterministic cleanup in <see cref="AlpmSession.Dispose()" />: once <c>alpm_release</c> destroys the native handle,
  /// nothing can enter a thunk with the ctx value anymore, so the slot is returned to the runtime.
  /// </summary>
  [Fact]
  public void AlpmDispose_ReleasesTheCallbackContext()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.AlpmSession;
    var callback = alpm.Callbacks;
    callback.LogHandler = (_, _) => { };

    alpm.Dispose();

    Assert.False(IsContextHandleAllocated(callback),
      "The ctx handle was not freed although alpm_release destroyed the native handle.");
  }

  private static void CollectUntilDead(WeakReference reference)
  {
    for (var i = 0; i < 10 && reference.IsAlive; ++i)
    {
      GC.Collect();
      GC.WaitForPendingFinalizers();
    }
  }

  private static void DeleteWorkspace(string workspace)
  {
    if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
  }

  /// <summary>
  /// Test seam without production surface: <c>AlpmCallbacks._ctxHandle</c> is private, and its slot state is
  /// what the ctx lifetime contract is about.
  /// </summary>
  private static readonly FieldInfo CtxHandleField =
    typeof(AlpmCallbacks).GetField("_ctxHandle", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("AlpmCallbacks._ctxHandle was renamed; update this test seam.");

  private static bool IsContextHandleAllocated(AlpmCallbacks callback)
    => ((WeakGCHandle<AlpmCallbacks>)CtxHandleField.GetValue(callback)!).IsAllocated;
}
