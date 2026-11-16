#pragma warning disable SYSLIB1054
using System.Buffers;
using System.Runtime.InteropServices;
using Orson.Alpm.Interop;

namespace Orson.Alpm;

/// <summary>Expands the <c>(fmt, va_list)</c> payload libalpm hands to <c>alpm_cb_log</c>.</summary>
/// <remarks>
/// A C <c>va_list</c> cannot be represented directly in C#, so it is received as an opaque pointer. Short messages
/// use a stack buffer. Longer messages use a pooled buffer and a second copy of the original <c>va_list</c>.
/// </remarks>
internal static unsafe partial class LogMessageFormatter
{
  private const int StackBufferLength = 512;

  [LibraryImport("libc", EntryPoint = "vasprintf")]
  private static partial int Vasprintf(byte** buffer, byte* format, void* vaList);

  [LibraryImport("libc", EntryPoint = "vsnprintf")]
  private static partial int Vsnprintf(byte* buffer, nuint length, byte* format, void* vaList);

  /// <summary>Formats one log message and sends the UTF-8 bytes to the handler.</summary>
  internal static void Format(
    byte* format,
    void* vaList,
    AlpmCallbacks.LogMessageHandler handler,
    LogLevel level)
  {
    if (format == null || vaList == null)
    {
      handler(level, default);
      return;
    }

    var vaListSize = GetVaListSize();
    if (vaListSize == 0)
    {
      FormatWithVasprintf(format, vaList, handler, level);
      return;
    }

    byte* stackBuffer = stackalloc byte[StackBufferLength];
    var length = FormatInto(buffer: stackBuffer, StackBufferLength, format, vaList, vaListSize);
    if (length < 0) return;

    if (length < StackBufferLength)
    {
      handler(level, new ReadOnlySpan<byte>(stackBuffer, length));
      return;
    }

    var rentedBuffer = ArrayPool<byte>.Shared.Rent(length + 1);
    try
    {
      fixed (byte* buffer = rentedBuffer)
      {
        var secondLength = FormatInto(buffer, rentedBuffer.Length, format, vaList, vaListSize);
        if (secondLength < 0 || secondLength >= rentedBuffer.Length) return;

        handler(level, new ReadOnlySpan<byte>(buffer, secondLength));
      }
    }
    finally
    {
      ArrayPool<byte>.Shared.Return(rentedBuffer);
    }
  }

  private static int FormatInto(
    byte* buffer,
    int bufferLength,
    byte* format,
    void* vaList,
    int vaListSize)
  {
    byte* vaListCopy = stackalloc byte[vaListSize];
    Buffer.MemoryCopy(vaList, vaListCopy, vaListSize, vaListSize);
    return Vsnprintf(buffer, (nuint)bufferLength, format, vaListCopy);
  }

  private static void FormatWithVasprintf(
    byte* format,
    void* vaList,
    AlpmCallbacks.LogMessageHandler handler,
    LogLevel level)
  {
    byte* buffer = null;
    if (Vasprintf(&buffer, format, vaList) < 0) return;

    try
    {
      var utf8 = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(buffer);
      handler(level, utf8);
    }
    finally
    {
      UnmanagedDestructors.CFree(buffer);
    }
  }

  private static int GetVaListSize() => RuntimeInformation.ProcessArchitecture switch
  {
    Architecture.X64 => 24,
    Architecture.Arm64 => 32,
    _ => 0,
  };
}
