using System.Runtime.CompilerServices;
using System.Text;
using Orson.Alpm.Bindings;
using Orson.Alpm.Interop;

namespace Orson.Alpm.Tests.Unit.Interop;

public sealed class TransientAlpmStringListAllocatorTests
{
  private static unsafe bool PointerInRange(void* start, int byteLength, void* target)
  {
    var s = (byte*)start;
    var t = (byte*)target;
    return t >= s && t < s + byteLength;
  }

  private static unsafe void AssertValidStringList(
    _alpm_list_t* head,
    ReadOnlySpan<string> expected,
    void* builderAddr = null,
    int builderSize = 0,
    bool? expectStackNodes = null,
    bool? expectStackStrings = null,
    bool checkContiguousStrings = false)
  {
    Assert.True(head != null);

    _alpm_list_t* prev = null;
    byte* expectedNextData = null;
    var node = head;

    foreach (var t in expected)
    {
      Assert.NotNull(node);
      if (prev != null) Assert.True(node->prev == prev);
      if (expectStackNodes.HasValue)
        Assert.Equal(expectStackNodes.Value, PointerInRange(builderAddr, builderSize, node));

      Assert.True(node->data != null);
      if (expectStackStrings.HasValue)
        Assert.Equal(expectStackStrings.Value, PointerInRange(builderAddr, builderSize, node->data));

      var utf8 = Encoding.UTF8.GetBytes(t);
      var data = (byte*)node->data;

      Assert.True(new ReadOnlySpan<byte>(data, utf8.Length).SequenceEqual(utf8));
      Assert.Equal((byte)0, data[utf8.Length]);
      Assert.Equal(t, Encoding.UTF8.GetString(data, utf8.Length));

      if (checkContiguousStrings && expectedNextData != null)
        Assert.True(data == expectedNextData);

      expectedNextData = data + utf8.Length + 1;
      prev = node;
      node = node->next;
    }

    Assert.Null(node);
    Assert.True(head->prev == prev);
  }

  // Samples uniformly across the 4 UTF-8 byte widths; uniform scalar sampling would bias ~94% to 4-byte runes.
  private static string CreateRandomUtf8String(Random rng, int runeCount)
  {
    var sb = new StringBuilder(runeCount * 2); // Up to 2 UTF-16 chars per Rune (surrogate pairs).
    Span<char> utf16 = stackalloc char[2];

    for (var i = 0; i < runeCount; i++)
    {
      var scalar = rng.Next(4) switch
      {
        0 => rng.Next(0x0001, 0x0080),    // 1-byte UTF-8 (U+0001..U+007F); excludes U+0000 (C-string terminator).
        1 => rng.Next(0x0080, 0x0800),    // 2-byte UTF-8 (U+0080..U+07FF).
        2 => NextThreeByteScalar(rng),    // 3-byte UTF-8 (U+0800..U+FFFF), skipping UTF-16 surrogates.
        _ => rng.Next(0x10000, 0x110000), // 4-byte UTF-8 (U+10000..U+10FFFF).
      };

      var written = new Rune(scalar).EncodeToUtf16(utf16);
      sb.Append(utf16[..written]);
    }

    return sb.ToString();

    static int NextThreeByteScalar(Random rng)
    {
      // Skip the 2,048 invalid surrogate code points (U+D800..U+DFFF, span 0x0800) by shrinking the upper bound
      // from 0x10000 to 0xF800 and shifting [0xD800, 0xF800) onto [0xE000, 0x10000).
      var raw = rng.Next(0x0800, 0xF800);
      return raw >= 0xD800 ? raw + 0x0800 : raw;
    }
  }

  [Fact]
  public unsafe void TransientAlpmStringListBuilder_EmptyInput_ReturnsNullAndConsumesNoCapacity()
  {
    string[] exactFull = [new string('ü', 250), "🧪", "α", "ab"]; // 4 nodes, 501 + 5 + 3 + 3 = 512 bytes

    using var builder = new TransientAlpmStringListAllocator();
    var builderAddr = Unsafe.AsPointer(in builder);
    var builderSize = sizeof(TransientAlpmStringListAllocator);

    var empty = builder.ConstructNative([]);
    var full = builder.ConstructNative(exactFull);

    Assert.Null(empty);
    AssertValidStringList(full, exactFull, builderAddr, builderSize, expectStackNodes: true, expectStackStrings: true);
  }

  [Fact]
  public unsafe void TransientAlpmStringListBuilder_Singleton_SetsSelfBackLinkAndNullNext()
  {
    string[] single = ["singleton-αβ🧪"];

    using var builder = new TransientAlpmStringListAllocator();
    var builderAddr = Unsafe.AsPointer(in builder);
    var builderSize = sizeof(TransientAlpmStringListAllocator);

    var list = builder.ConstructNative(single);

    AssertValidStringList(list, single, builderAddr, builderSize, expectStackNodes: true, expectStackStrings: true);
  }

  [Fact]
  public unsafe void TransientAlpmStringListBuilder_PreservesOrderAndLoopsBack()
  {
    string[] strings = ["first", "αβγ", "third-🧪"];

    using var builder = new TransientAlpmStringListAllocator();
    var first = builder.ConstructNative(strings);

    AssertValidStringList(first, strings);
  }

  [Fact]
  public unsafe void TransientAlpmStringListBuilder_SupportsMultipleListBuilding()
  {
    string[] strings1 = ["first", "αβ", "🧪"];
    string[] strings2 = ["日本語", "second"];
    string[] strings3 = ["foo", "bar-é", "baz"];

    using var builder = new TransientAlpmStringListAllocator();

    var firstNative = builder.ConstructNative(strings1);
    var secondNative = builder.ConstructNative(strings2);
    var thirdNative = builder.ConstructNative(strings3);

    AssertValidStringList(firstNative, strings1);
    AssertValidStringList(secondNative, strings2);
    AssertValidStringList(thirdNative, strings3);
  }

  [Fact]
  public unsafe void TransientAlpmStringListBuilder_PlacesSmallListsOnStack()
  {
    string[] strings = ["foo", "αβγ", "🧪"];

    using var builder = new TransientAlpmStringListAllocator();
    var builderAddr = Unsafe.AsPointer(in builder);
    var builderSize = sizeof(TransientAlpmStringListAllocator);

    var list = builder.ConstructNative(strings);

    AssertValidStringList(
      list,
      strings,
      builderAddr,
      builderSize,
      expectStackNodes: true,
      expectStackStrings: true);
  }

  [Theory]
  [InlineData(9)]
  [InlineData(42)]
  [InlineData(325)]
  [InlineData(1337)]
  public unsafe void TransientAlpmStringListBuilder_RandomUtf8Strings_RoundTripPackAndTerminate(int seed)
  {
    var rng = new Random(seed);
    var strings = new string[4];
    for (var i = 0; i < strings.Length; i++)
      strings[i] = CreateRandomUtf8String(rng, rng.Next(0, 12));

    using var builder = new TransientAlpmStringListAllocator();
    var builderAddr = Unsafe.AsPointer(in builder);
    var builderSize = sizeof(TransientAlpmStringListAllocator);

    var list = builder.ConstructNative(strings);

    AssertValidStringList(
      list,
      strings,
      builderAddr,
      builderSize,
      expectStackNodes: true,
      expectStackStrings: true,
      checkContiguousStrings: true);
  }

  [Fact]
  public unsafe void TransientAlpmStringListBuilder_Utf8Sizing_PacksMixedWidthsAndSpillsByByteCount()
  {
    string[] mixed = ["", "ascii", "αβγ", "🧪🚀"];

    using var stackBuilder = new TransientAlpmStringListAllocator();
    var stackAddr = Unsafe.AsPointer(in stackBuilder);
    var stackSize = sizeof(TransientAlpmStringListAllocator);

    var packed = stackBuilder.ConstructNative(mixed);

    AssertValidStringList(
      packed,
      mixed,
      stackAddr,
      stackSize,
      expectStackNodes: true,
      expectStackStrings: true,
      checkContiguousStrings: true);

    string[] multibyteOverflow = [new string('日', 200)]; // 200 UTF-16 chars < 512, 601 UTF-8 bytes > 512

    using var spillBuilder = new TransientAlpmStringListAllocator();
    var spillAddr = Unsafe.AsPointer(in spillBuilder);
    var spillSize = sizeof(TransientAlpmStringListAllocator);

    var spilled = spillBuilder.ConstructNative(multibyteOverflow);

    AssertValidStringList(
      spilled,
      multibyteOverflow,
      spillAddr,
      spillSize,
      expectStackNodes: true,
      expectStackStrings: false);
  }

  [Fact]
  public unsafe void TransientAlpmStringListBuilder_SuccessfullyInterleavesStackAndHeapAllocation()
  {
    string[] list1 = ["a-α", "b-🧪"];
    string[] list2 = [new string('日', 200)];
    string[] list3 = ["c", "δ-ε", "ζ"];
    string[] list4 = [new string('ü', 300), new string('中', 200)];
    string[] list5 = ["final-Ω"];

    using var builder = new TransientAlpmStringListAllocator();
    var builderAddr = Unsafe.AsPointer(in builder);
    var builderSize = sizeof(TransientAlpmStringListAllocator);

    var nativeList1 = builder.ConstructNative(list1);
    var nativeList2 = builder.ConstructNative(list2);
    var nativeList3 = builder.ConstructNative(list3);
    var nativeList4 = builder.ConstructNative(list4);
    var nativeList5 = builder.ConstructNative(list5);

    AssertValidStringList(nativeList1, list1, builderAddr, builderSize, expectStackNodes: true, expectStackStrings: true);
    AssertValidStringList(nativeList2, list2, builderAddr, builderSize, expectStackNodes: true, expectStackStrings: false);
    AssertValidStringList(nativeList3, list3, builderAddr, builderSize, expectStackNodes: false, expectStackStrings: true);
    AssertValidStringList(nativeList4, list4, builderAddr, builderSize, expectStackNodes: false, expectStackStrings: false);
    AssertValidStringList(nativeList5, list5, builderAddr, builderSize, expectStackNodes: true, expectStackStrings: true);
  }

  [Fact]
  public unsafe void TransientAlpmStringListBuilder_ExactNodeBoundaries_SelectExpectedStorage()
  {
    string[] seed = ["seed-α"];
    string[] exactRemaining = ["n1-β", "n2-γ", "n3-🧪"];
    string[] zeroLeftOverflow = ["over-δ"];

    using var exactBuilder = new TransientAlpmStringListAllocator();
    var exactAddr = Unsafe.AsPointer(in exactBuilder);
    var exactSize = sizeof(TransientAlpmStringListAllocator);

    var seedList = exactBuilder.ConstructNative(seed);
    var exactList = exactBuilder.ConstructNative(exactRemaining);
    var overZeroList = exactBuilder.ConstructNative(zeroLeftOverflow);

    AssertValidStringList(seedList, seed, exactAddr, exactSize, expectStackNodes: true, expectStackStrings: true);
    AssertValidStringList(exactList, exactRemaining, exactAddr, exactSize, expectStackNodes: true, expectStackStrings: true);
    AssertValidStringList(overZeroList, zeroLeftOverflow, exactAddr, exactSize, expectStackNodes: false, expectStackStrings: true);

    string[] oneOverRemaining = ["a", "b", "c", "d"];

    using var spillBuilder = new TransientAlpmStringListAllocator();
    var spillAddr = Unsafe.AsPointer(in spillBuilder);
    var spillSize = sizeof(TransientAlpmStringListAllocator);

    var spillSeed = spillBuilder.ConstructNative(seed);
    var spilled = spillBuilder.ConstructNative(oneOverRemaining);

    AssertValidStringList(spillSeed, seed, spillAddr, spillSize, expectStackNodes: true, expectStackStrings: true);
    AssertValidStringList(spilled, oneOverRemaining, spillAddr, spillSize, expectStackNodes: false, expectStackStrings: true);
  }

  [Fact]
  public unsafe void TransientAlpmStringListBuilder_ExactUtf8ByteBoundaries_SelectExpectedStorage()
  {
    string[] seed = ["α"]; // 3 bytes used, 509 remaining
    string[] exactRemaining = ["🧪", new string('ü', 251) + "a"]; // 5 + 504 = 509 bytes
    string[] zeroLeftOverflow = ["x"];

    using var exactBuilder = new TransientAlpmStringListAllocator();
    var exactAddr = Unsafe.AsPointer(in exactBuilder);
    var exactSize = sizeof(TransientAlpmStringListAllocator);

    var seedList = exactBuilder.ConstructNative(seed);
    var exactList = exactBuilder.ConstructNative(exactRemaining);
    var overZeroList = exactBuilder.ConstructNative(zeroLeftOverflow);

    AssertValidStringList(seedList, seed, exactAddr, exactSize, expectStackNodes: true, expectStackStrings: true);
    AssertValidStringList(exactList, exactRemaining, exactAddr, exactSize, expectStackNodes: true, expectStackStrings: true);
    AssertValidStringList(overZeroList, zeroLeftOverflow, exactAddr, exactSize, expectStackNodes: true, expectStackStrings: false);

    string[] oneOverRemaining = ["🧪", new string('ü', 252)]; // 5 + 505 = 510 bytes (254 UTF-16 chars)

    using var spillBuilder = new TransientAlpmStringListAllocator();
    var spillAddr = Unsafe.AsPointer(in spillBuilder);
    var spillSize = sizeof(TransientAlpmStringListAllocator);

    var spillSeed = spillBuilder.ConstructNative(seed);
    var spilled = spillBuilder.ConstructNative(oneOverRemaining);

    AssertValidStringList(spillSeed, seed, spillAddr, spillSize, expectStackNodes: true, expectStackStrings: true);
    AssertValidStringList(spilled, oneOverRemaining, spillAddr, spillSize, expectStackNodes: true, expectStackStrings: false);
  }
}
