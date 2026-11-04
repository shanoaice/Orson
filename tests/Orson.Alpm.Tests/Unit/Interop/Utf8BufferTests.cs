using System.Text;

namespace Orson.Alpm.Tests.Unit.Interop;

public sealed unsafe class Utf8BufferTests
{
  [Fact]
  public void NullString_YieldsNullPointer()
  {
    Span<byte> scratch = stackalloc byte[16];
    using var buf = new Utf8Buffer(null, scratch);
    Assert.True(buf.Ptr == null);
  }

  [Fact]
  public void EmptyString_UsesScratchAndWritesNulTerminator()
  {
    Span<byte> scratch = stackalloc byte[8];
    scratch.Fill(0xFF);
    using var buf = new Utf8Buffer("", scratch);
    Assert.Equal(0, *buf.Ptr);
    Assert.Equal("", NativeString.FromNative((nint)buf.Ptr));
  }

  [Theory]
  [InlineData(15, true)]
  [InlineData(16, false)]
  [InlineData(17, false)]
  [InlineData(512, false)]
  public void StringAtAndAcrossScratchBoundary_RoundTripsUtf8AndNulTerminates(int length, bool expectInScratch)
  {
    var value = new string('x', length);
    Span<byte> scratch = stackalloc byte[16];
    fixed (byte* scratchPtr = scratch)
    {
      using var buf = new Utf8Buffer(value, scratch);

      var isInScratch = (nint)buf.Ptr >= (nint)scratchPtr && (nint)buf.Ptr < (nint)scratchPtr + scratch.Length;
      Assert.Equal(expectInScratch, isInScratch);

      var byteCount = Encoding.UTF8.GetByteCount(value);
      Assert.Equal(0, buf.Ptr[byteCount]);
      Assert.Equal(value, NativeString.FromNative((nint)buf.Ptr));
    }
  }

  [Fact]
  public void MultiByteUtf8_SpillsToHeapWhenByteCountExceedsScratch()
  {
    const string value = "🚀🎉中文";
    var byteCount = Encoding.UTF8.GetByteCount(value);
    Assert.Equal(14, byteCount);
    Assert.Equal(6, value.Length);

    Span<byte> scratch = stackalloc byte[10];
    fixed (byte* scratchPtr = scratch)
    {
      using var buf = new Utf8Buffer(value, scratch);

      var isInScratch = (nint)buf.Ptr >= (nint)scratchPtr && (nint)buf.Ptr < (nint)scratchPtr + scratch.Length;
      Assert.False(isInScratch);
      Assert.Equal(0, buf.Ptr[byteCount]);
      Assert.Equal(value, NativeString.FromNative((nint)buf.Ptr));
    }
  }
}
