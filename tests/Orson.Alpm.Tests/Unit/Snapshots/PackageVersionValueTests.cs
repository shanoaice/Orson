using System.Runtime.InteropServices;
using System.Text;

namespace Orson.Alpm.Tests.Unit.Snapshots;

/// <summary>
/// Pins the value semantics of <see cref="PackageVersion" />: equality is ordinal on the copied version text, and
/// it is deliberately a different relation from <see cref="PackageVersion.CompareTo" /> returning 0.
/// </summary>
/// <remarks>
/// Every version here is built over a buffer that is freed before the assertions run, so nothing in these tests
/// can pass by reading native memory that happens to still be readable. No libalpm handle is needed:
/// <c>alpm_pkg_vercmp</c> takes no handle.
/// </remarks>
public sealed unsafe class PackageVersionValueTests
{
  /// <summary>
  /// Copies <paramref name="text" /> into native memory, snapshots it, and releases the buffer - the snapshot has
  /// to stand on its own from here on.
  /// </summary>
  private static PackageVersion Version(string text)
  {
    var buffer = NativeString.ToNative(text);

    try
    {
      return new PackageVersion(buffer);
    }
    finally
    {
      NativeMemory.Free(buffer);
    }
  }

  [Fact]
  public void SameText_TwoInstances_AreEqual()
  {
    var left = Version("1.2.3-4");
    var right = Version("1.2.3-4");

    Assert.True(left.Equals(right));
    Assert.True(left.Equals((object)right));
    Assert.True(left == right);
    Assert.False(left != right);
    Assert.Equal(left, right);
    Assert.Equal(left.GetHashCode(), right.GetHashCode());
  }

  [Fact]
  public void DifferentText_IsNotEqual()
  {
    var older = Version("1.0-1");
    var newer = Version("1.0-2");

    Assert.False(older.Equals(newer));
    Assert.False(older == newer);
    Assert.True(older != newer);
    Assert.NotEqual(older, newer);
  }

  [Fact]
  public void NullAndForeignObjects_AreNotEqual()
  {
    var version = Version("1.0-1");

    Assert.False(version.Equals(null));
    Assert.False(version.Equals((object?)null));
    // ReSharper disable once SuspiciousTypeConversion.Global
    Assert.False(version.Equals("1.0-1"));
    // Intentionally testing overloaded operator == and != against null
#pragma warning disable xUnit2024, CS8073
    // ReSharper disable ConditionIsAlwaysTrueOrFalse
    Assert.False(version == null);
    Assert.False(null == version);
    Assert.True(version != null);
    Assert.True(null != version);
    // ReSharper restore ConditionIsAlwaysTrueOrFalse
#pragma warning restore xUnit2024, CS8073

    PackageVersion? leftNull = null;
    PackageVersion? rightNull = null;

    Assert.True(leftNull == rightNull);
    Assert.False(leftNull != rightNull);
  }

  /// <summary>
  /// The payoff of the equality members: a version read from one package finds the entry a version read from
  /// another package put in the collection.
  /// </summary>
  [Fact]
  public void CollectionLookup_FindsAnIndependentInstance()
  {
    var set = new HashSet<PackageVersion> { Version("1.0-1") };
    var counts = new Dictionary<PackageVersion, int> { [Version("1.0-1")] = 1 };

    Assert.Contains(Version("1.0-1"), set);
    Assert.DoesNotContain(Version("1.0-2"), set);
    Assert.Equal(1, counts[Version("1.0-1")]);

    // A second insert of an equal key replaces the entry rather than adding one.
    counts[Version("1.0-1")] = 2;
    Assert.Single(counts);
    Assert.Equal(2, counts[Version("1.0-1")]);
  }

  /// <summary>
  /// Locks the divergence the type remarks document. Probed against libalpm 16.0.1: a missing package release is
  /// not compared, so <c>"1.0"</c> compares equal to <c>"1.0-1"</c> and to <c>"1.0-2"</c>, while those two do not compare
  /// equal to each other. That relation is not transitive, which is exactly why it cannot be equality.
  /// </summary>
  [Fact]
  public void CompareToZero_DoesNotImplyEquality()
  {
    var noRelease = Version("1.0");
    var firstRelease = Version("1.0-1");
    var secondRelease = Version("1.0-2");

    Assert.Equal(0, noRelease.CompareTo(firstRelease));
    Assert.Equal(0, noRelease.CompareTo(secondRelease));

    Assert.False(noRelease.Equals(firstRelease));
    Assert.False(noRelease == firstRelease);

    // The same pair under an ordering that does compare both releases.
    Assert.True(firstRelease.CompareTo(secondRelease) < 0);
    Assert.False(firstRelease.Equals(secondRelease));
  }

  /// <summary>
  /// Sorting follows <see cref="PackageVersion.CompareTo" />, not equality, so a collection ordered by the default
  /// comparer still groups by libalpm's ordering.
  /// </summary>
  [Fact]
  public void Sorting_UsesTheOrderingRatherThanEquality()
  {
    var versions = new List<PackageVersion> { Version("1.0-2"), Version("1.0-1"), Version("0.9-9") };

    versions.Sort();

    Assert.Equal(["0.9-9", "1.0-1", "1.0-2"], versions.Select(version => version.ToString()));
  }

  [Fact]
  public void SmallBufferOptimization_InlineAndHeapFallback()
  {
    // <= 30 bytes uses inline buffer
    var inlineVersion = new PackageVersion("1.2.3-1");
    Assert.Equal("1.2.3-1", inlineVersion.ToString());
    Assert.True(inlineVersion.Utf8Span.SequenceEqual("1.2.3-1"u8));

    // > 30 bytes uses heap fallback
    const string longVersion = "1.2.3.4.5.6.7.8.9.10.11.12.13.14.15-1";
    Assert.True(Encoding.UTF8.GetByteCount(longVersion) > 30);
    var heapVersion = new PackageVersion(longVersion);
    Assert.Equal(longVersion, heapVersion.ToString());
    Assert.True(heapVersion.Utf8Span.SequenceEqual(Encoding.UTF8.GetBytes(longVersion)));

    // > 255 bytes uses heap fallback and is not limited by a byte length field
    var hugeVersionString = new string('1', 300) + "-1";
    var hugeVersion = new PackageVersion(hugeVersionString);
    Assert.Equal(hugeVersionString, hugeVersion.ToString());
    Assert.Equal(302, hugeVersion.Utf8Span.Length);
    Assert.True(hugeVersion.Utf8Span.SequenceEqual(Encoding.UTF8.GetBytes(hugeVersionString)));
  }

  [Fact]
  public void Comparisons_AcrossInlineAndHeap()
  {
    var v1 = new PackageVersion("1.0.0-1");
    var v2 = new PackageVersion("1.0.0-2");
    Assert.True(v1.CompareTo(v2) < 0);
    Assert.True(v2.CompareTo(v1) > 0);

    var longOlder = new PackageVersion("1.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0-1");
    var longNewer = new PackageVersion("1.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0-2");
    Assert.True(longOlder.CompareTo(longNewer) < 0);
    Assert.True(longNewer.CompareTo(longOlder) > 0);

    Assert.True(v1.CompareTo(longOlder) != 0 || v1.Equals(longOlder) == false);
  }

  [Fact]
  public void TryFormat_CharAndUtf8Span()
  {
    var version = new PackageVersion("2.4.6-1");

    // ISpanFormattable (chars)
    Span<char> charBuf = stackalloc char[16];
    Assert.True(version.TryFormat(charBuf, out var charsWritten, default, null));
    Assert.Equal("2.4.6-1", charBuf[..charsWritten].ToString());

    Span<char> tooSmallChar = stackalloc char[3];
    Assert.False(version.TryFormat(tooSmallChar, out _, default, null));

    // IUtf8SpanFormattable (bytes)
    Span<byte> byteBuf = stackalloc byte[16];
    Assert.True(version.TryFormat(byteBuf, out var bytesWritten, default, null));
    Assert.True(byteBuf[..bytesWritten].SequenceEqual("2.4.6-1"u8));

    Span<byte> tooSmallByte = stackalloc byte[3];
    Assert.False(version.TryFormat(tooSmallByte, out _, default, null));
  }

  [Fact]
  public void Constructors_FromSpanAndEmpty()
  {
    var fromSpan = new PackageVersion("1.0"u8);
    Assert.Equal("1.0", fromSpan.ToString());

    var empty = new PackageVersion("");
    Assert.Equal(string.Empty, empty.ToString());
    Assert.True(empty.Utf8Span.IsEmpty);

    var defaultVersion = default(PackageVersion);
    Assert.Equal(string.Empty, defaultVersion.ToString());
    Assert.True(defaultVersion.Utf8Span.IsEmpty);
  }
}

