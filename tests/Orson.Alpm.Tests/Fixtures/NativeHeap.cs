using System.Runtime.InteropServices;

namespace Orson.Alpm.Tests.Fixtures;

/// <summary>
/// Reads the native heap in use, as reported by glibc's <c>mallinfo2</c>.
/// </summary>
/// <remarks>
/// <para>
/// The value covers every <c>malloc</c> arena in the process, including chunks served from <c>mmap</c>
/// (<see cref="MallInfo2.Hblkhd" />). The managed GC heap is not part of it: the runtime backs that heap with
/// <c>mmap</c> directly, not through <c>malloc</c>.
/// </para>
/// <para>
/// Readings are only comparable within one process and one allocator. Any thread that allocates through
/// <c>malloc</c> moves the number, so a test that compares two readings must not overlap with other tests.
/// Put it in the <c>ProcessWideNativeHeap</c> collection.
/// </para>
/// </remarks>
internal static partial class NativeHeap
{

  /// <summary>Bytes currently allocated by the process's native allocator.</summary>
  /// <remarks>
  /// Pending finalizers are flushed first, so a finalizer that has not run yet cannot look like a leak. The flush
  /// itself does not lower the reported value, because the collected memory is not the measured heap.
  /// </remarks>
  internal static long InUseBytes()
  {
    GC.Collect();
    GC.WaitForPendingFinalizers();

    var info = MallInfo2Native();
    return (long)(info.Uordblks + info.Hblkhd);
  }

  // Declaration order is the ABI: glibc passes mallinfo2 by value in struct registers.
  [StructLayout(LayoutKind.Sequential)]
  private struct MallInfo2
  {
    public nuint Arena, Ordblks, Smblks, Hblks, Hblkhd, Usmblks, Fsmblks, Uordblks, Fordblks, Keepcost;
  }

  [LibraryImport("libc", EntryPoint = "mallinfo2")]
  private static partial MallInfo2 MallInfo2Native();
}
