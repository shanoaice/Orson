using System.Text;

namespace Orson.Alpm.Tests.Unit.Callbacks;

public sealed unsafe class Utf8StringCacheTests
{
  [Fact]
  public void GetOrCreate_ReusesTheStringForEqualUtf8()
  {
    var cache = new Utf8StringCache();
    ReadOnlySpan<byte> utf8 = "中文-package"u8;

    var first = cache.GetOrCreate(utf8);
    var second = cache.GetOrCreate(utf8);

    Assert.Same(first, second);
    Assert.Equal("中文-package", first);
  }

  [Fact]
  public void GetOrCreate_ReplacesTheStringWhenTheUtf8Changes()
  {
    var cache = new Utf8StringCache();

    var first = cache.GetOrCreate("first"u8);
    var second = cache.GetOrCreate("second"u8);

    Assert.NotSame(first, second);
    Assert.Equal("first", first);
    Assert.Equal("second", second);
  }

  [Fact]
  public void GetOrCreate_GrowsBeyondTheInitialBuffer()
  {
    var cache = new Utf8StringCache();
    var text = new string('ü', 400);
    var utf8 = Encoding.UTF8.GetBytes(text);

    Assert.Equal(text, cache.GetOrCreate(utf8));
  }

  [Fact]
  public void GetOrCreate_ReturnsEmptyForANullNativeString()
  {
    var cache = new Utf8StringCache();

    Assert.Same(string.Empty, cache.GetOrCreate((byte*)null));
  }
}
