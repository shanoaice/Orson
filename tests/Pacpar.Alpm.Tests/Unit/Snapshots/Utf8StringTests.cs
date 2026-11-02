using System.Runtime.InteropServices;
using System.Text;
using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit.Snapshots;

/// <summary>
/// libalpm's string contract is UTF-8, so both the marshalling helpers and one
/// end-to-end option round trip must carry non-ASCII text through unchanged.
/// </summary>
/// <remarks>
/// On Linux this was already true of the old ANSI calls (<see cref="Encoding.Default"/> *is* UTF-8
/// there), so these tests pin the encoding choice rather than a behaviour change. They are the
/// portable half of the sweep: on a Windows host the old code would fail them.
/// </remarks>
public sealed unsafe class Utf8StringTests
{
  private const string NonAscii = "中文 / ünïcødé";

  [Fact]
  public void ToNative_WritesUtf8BytesAndNulTerminator()
  {
    var expected = Encoding.UTF8.GetBytes(NonAscii);
    var ptr = NativeString.ToNative(NonAscii);
    try
    {
      Assert.Equal(expected, new ReadOnlySpan<byte>(ptr, expected.Length).ToArray());
      Assert.Equal((byte)0, ptr[expected.Length]);
      Assert.Equal(NonAscii, NativeString.FromNative((nint)ptr));
    }
    finally
    {
      NativeMemory.Free(ptr);
    }
  }

  [Fact]
  public void ToNative_Null_IsANullPointer()
  {
    // Assert.Null cannot be used on unmanaged pointers (byte*).
#pragma warning disable xUnit2024
    // ReSharper disable ConditionIsAlwaysTrueOrFalse
    Assert.True(NativeString.ToNative(null) == null);
    // ReSharper restore ConditionIsAlwaysTrueOrFalse
#pragma warning restore xUnit2024
  }

  [Fact]
  public void FromNative_NullPointer_IsNull()
  {
    Assert.Null(NativeString.FromNative(0));
  }

  [Fact]
  public void NonAsciiOption_RoundTripsThroughLibalpm()
  {
    using var env = new IsolatedAlpmEnvironment();
    var architectures = env.AlpmSession.Options.Architectures;

    architectures.Add(NonAscii);

    Assert.Equal(NonAscii, Assert.Single(architectures));
  }

  [Theory]
  [InlineData("", "")]
  [InlineData("a\0b", "a")]
  [InlineData("𐍈🧪", "𐍈🧪")]
  public void ToAndFromNative_BoundaryStrings_RoundTripOrTruncateAtNul(string input, string expected)
  {
    var ptr = NativeString.ToNative(input);
    try
    {
      var roundTripped = NativeString.FromNative((nint)ptr);
      Assert.Equal(expected, roundTripped);
    }
    finally
    {
      NativeMemory.Free(ptr);
    }
  }
}
