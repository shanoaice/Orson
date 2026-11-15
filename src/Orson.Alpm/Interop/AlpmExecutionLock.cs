namespace Orson.Alpm.Interop;

/// <summary>
/// Process-wide execution lock protecting boundary operations that invoke libalpm initialization,
/// handle release, or other non-reentrant libalpm process state modifications.
/// </summary>
internal static class AlpmExecutionLock
{
  private static readonly Lock s_lock = new();

  /// <summary>Acquires the lock, blocking until available.</summary>
  internal static void Enter() => s_lock.Enter();

  /// <summary>Releases the lock.</summary>
  internal static void Exit() => s_lock.Exit();

  /// <summary>Acquires the lock and returns a scope that releases the lock when disposed.</summary>
  internal static Scope Hold()
  {
    s_lock.Enter();
    return new Scope(s_lock);
  }

  /// <summary>A disposable scope that releases the execution lock on disposal.</summary>
  internal readonly ref struct Scope : IDisposable
  {
    private readonly Lock? _lock;

    internal Scope(Lock @lock)
    {
      _lock = @lock;
    }

    public void Dispose() => _lock?.Exit();
  }
}
