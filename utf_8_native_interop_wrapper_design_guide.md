# UTF-8 Native Interop & Wrapper Design Guide for .NET

## Core Philosophy

In .NET, `System.String` is backed by UTF-16, while most C, C++, and Rust native libraries expect null-terminated UTF-8 (`const char*`). When building CLI tools, GUI applications, or wrapper libraries around native engines, the primary performance bottleneck is rarely the CPU cost of UTF-16 $\leftrightarrow$ UTF-8 transcoding (which is SIMD-vectorized in .NET); it is **heap allocation and GC pressure**.

The optimal architecture treats **UTF-8 as your wire and native-engine format** and **`System.String` as your human-interface format**—eliminating heap allocations on hot paths without sacrificing idiomatic C# ergonomics.

---

## 1. Architectural Boundary Matrix

| Layer / Operation | Optimal Representation | Conversion Strategy | Ergonomic Cost |
| :--- | :--- | :--- | :--- |
| **Native constants & tags** | `"literal"u8` (`ReadOnlySpan<byte>`) | None (null-terminated at compile time) | None |
| **File / Network / IPC payloads** | `byte[]` / `ReadOnlySpan<byte>` / `PipeReader` | None (pass raw slices directly to native API) | Low |
| **Native $\rightarrow$ C# $\rightarrow$ Native IDs/Keys** | `nint` / `readonly struct` cursor | Lazy (`Get*()` or `ToString()` only on UI display) | Low |
| **Dynamic native logs / telemetry** | `Span<byte>` + `Utf8.TryWrite` | Direct UTF-8 formatting (`IUtf8SpanFormattable`) | Low |
| **High-volume CLI stdout** | `ReadOnlySpan<byte>` | Write directly to `Console.OpenStandardOutput()` | Low |
| **Interactive GUI inputs & CLI args** | `System.String` | Stack-allocated marshalling (`LibraryImport`) | None |
| **GUI Data Binding & Text Controls** | `System.String` | Materialize at the ViewModel / UI boundary | None |

---

## 2. Essential Zero-Allocation UTF-8 Patterns

### Compile-Time UTF-8 Literals (`"..."u8`)
C# stores UTF-8 string literals in the assembly's static data section with an **implicit trailing `\0` byte**. They can be passed directly to native C functions expecting `const char*` with zero allocation and zero copying:

```csharp
[LibraryImport("native_engine")]
private static partial void engine_set_label(in byte utf8NullTerminatedName);

// Zero-allocation, zero-conversion call
engine_set_label(in MemoryMarshal.GetReference("main_viewport"u8));
```

### Zero-Allocation UTF-8 Formatting (`Utf8.TryWrite`)
Use `System.Text.Unicode.Utf8.TryWrite` and `IUtf8SpanFormattable` to format numbers, dates, and identifiers directly into a stack-allocated or pooled UTF-8 buffer:

```csharp
Span<byte> buffer = stackalloc byte[128];
if (Utf8.TryWrite(buffer, $"frame={frameIndex};dt={deltaTime:F3}\0", out int written))
{
    native_log(in MemoryMarshal.GetReference(buffer));
}
```

### Zero-GC Marshalling for Interactive User Input (`[LibraryImport]`)
When starting from a UI `string` (such as a search query or CLI argument), do not try to rewrite your UI layer in UTF-8. Instead, use `[LibraryImport]` with `StringMarshalling.Utf8`, which uses `Utf8StringMarshaller` to encode short strings (~256 UTF-8 bytes or less) onto a `stackalloc` buffer with zero heap allocation:

```csharp
[LibraryImport("native_engine", StringMarshalling = StringMarshalling.Utf8)]
private static partial int engine_search(string query);
```

### Raw CLI Output & Stream Bridging
* **High-throughput CLI output:** Bypass `Console.WriteLine(string)` and write UTF-8 spans directly to `Console.OpenStandardOutput()`.
* **In-memory `string` to `Stream` (.NET 11+):** Use `System.IO.StringStream` to pass a `string` to a `Stream`-based consumer with incremental UTF-8 encoding rather than allocating a full `byte[]` copy upfront.

---

## 3. Designing Native Wrapper Libraries (Lightweight Views vs. Models)

When wrapping a native system library (such as a package manager, database, or AST walker) that scans tens of thousands of records, avoid eager UTF-16 allocation and avoid putting nullable `string?` fields inside value types.

### Why `readonly struct` Views Should Stay Purely Unmanaged
Keep lightweight handle wrappers as small, unmanaged `readonly struct` types containing only native pointers (e.g., 16 bytes for `nint handle` + `nint ownerHandle`):
1. **No GC Tracking:** Pure unmanaged structs have zero GC write barriers and are never scanned by the garbage collector.
2. **CPU Register Passing:** Small structs pass directly in CPU registers across method boundaries.
3. **No Defensive Copies:** Making a struct mutable to support lazy `_name ??= ...` fields causes the C# compiler to emit silent defensive copies whenever the struct is stored in a `readonly` field, passed via `in`, or read from a `ReadOnlySpan<T>`—wasting the cached string anyway.

### Field-by-Field Exposure Strategy

| Field Characteristics | Examples | Recommended API Design |
| :--- | :--- | :--- |
| **Scanned in tight loops** | `Name`, `Version`, `Provides` | `ReadOnlySpan<byte>` property (`NameUtf8`) + explicit `GetName()` method |
| **High-volume collections** | Owned file paths, dependency lists | Custom `ref struct` enumerator yielding `ReadOnlySpan<byte>` |
| **Low cardinality (many duplicates)** | `Arch`, `Repository`, `License` | `ReadOnlySpan<byte>` + database-level string interning pool |
| **High cardinality, rarely read** | `Description`, `URL`, `Maintainer` | Uncached `GetDescription()` method (decodes on demand, zero struct bloat) |
| **Retained for UI / async / JSON** | Selected item details | Promote via `.ToPackage()` into a heap-allocated `sealed class` |

### Reference Implementation

```csharp
public readonly unsafe struct PackageView(nint handle, PackageDatabase db)
{
    // 1. Hot-path property: zero allocation, points directly to native memory
    public ReadOnlySpan<byte> NameUtf8
    {
        get
        {
            db.ThrowIfDisposed();
            byte* ptr = NativeMethods.pkg_get_name(handle);
            return MemoryMarshal.CreateReadOnlySpanFromNullTerminated(ptr);
        }
    }

    public ReadOnlySpan<byte> ArchUtf8
    {
        get
        {
            db.ThrowIfDisposed();
            byte* ptr = NativeMethods.pkg_get_arch(handle);
            return MemoryMarshal.CreateReadOnlySpanFromNullTerminated(ptr);
        }
    }

    // 2. Explicit Get*() methods signal allocation to callers
    public string GetName() => Encoding.UTF8.GetString(NameUtf8);

    // 3. Low-cardinality fields use a pool owned by the heap-allocated database
    public string GetArchitecture() => db.InternUtf8(ArchUtf8);

    // 4. Delegate domain comparisons directly to native UTF-8 functions
    public int CompareVersionTo(ReadOnlySpan<byte> otherNullTerminatedUtf8)
    {
        db.ThrowIfDisposed();
        return NativeMethods.pkg_vercmp(
            in MemoryMarshal.GetReference(VersionUtf8),
            in MemoryMarshal.GetReference(otherNullTerminatedUtf8));
    }

    // 5. Promote to a managed class when data must outlive the scan or cross async boundaries
    public Package ToPackage() => new(GetName(), GetArchitecture(), GetDescription());
}
```

### Zero-Allocation String Pooling on the Owner Class
For repeated metadata like architectures or repository names, pool strings on the heap-allocated `PackageDatabase` using `HashSet<string>.GetAlternateLookup<ReadOnlySpan<byte>>()` (.NET 9+):

```csharp
private readonly HashSet<string> _stringPool = new(Utf8StringComparer.Instance);

internal string InternUtf8(ReadOnlySpan<byte> utf8)
{
    if (utf8.IsEmpty) return string.Empty;

    var lookup = _stringPool.GetAlternateLookup<ReadOnlySpan<byte>>();
    if (lookup.TryGetValue(utf8, out string? cached))
        return cached;

    string created = Encoding.UTF8.GetString(utf8);
    _stringPool.Add(created);
    return created;
}
```

### `ref struct` Enumerators for Native Linked Lists
When a native API exposes lists of strings (such as package file trees), return a custom `ref struct` enumerator instead of `IEnumerable<string>`:

```csharp
public ref struct PackageFileEnumerator
{
    private nint _node;
    public ReadOnlySpan<byte> Current { get; private set; }

    internal PackageFileEnumerator(nint headNode) => _node = headNode;
    public PackageFileEnumerator GetEnumerator() => this;

    public bool MoveNext()
    {
        if (_node == 0) return false;
        unsafe
        {
            byte* pathPtr = NativeMethods.list_get_data(_node);
            Current = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(pathPtr);
        }
        _node = NativeMethods.list_next(_node);
        return true;
    }
}
```

### Memory Safety Guardrails
1. **Guard against use-after-free:** Native spans are only valid while the owning database or transaction handle remains open. Always call `ObjectDisposedException.ThrowIf(...)` in span property getters.
2. **Watch for scratch-buffer overwrites:** If a C function returns a pointer to a thread-local or static internal buffer overwritten on subsequent calls, do not expose it as a `ReadOnlySpan<byte>` on a general struct—either copy it immediately or restrict it to a single-item `ref struct` cursor.

---

## 4. .NET Runtime Version Guide (.NET 10 LTS vs. .NET 11)

All core zero-allocation interop patterns work identically on **.NET 10 (LTS)** (with a minimum runtime floor of .NET 9 for `GetAlternateLookup`). Target `net10.0` by default, and optionally multi-target `net11.0` if you need its incremental additions:

| Feature / API | Minimum .NET | Notes |
| :--- | :--- | :--- |
| `readonly struct` unmanaged cursors & `ref struct` enumerators | .NET Core 2.1 | Zero GC tracking and compile-time escape safety. |
| `MemoryMarshal.CreateReadOnlySpanFromNullTerminated` | .NET 6 | SIMD-accelerated `strlen` scan over `byte*`. |
| `"..."u8` literals & `[LibraryImport(StringMarshalling.Utf8)]` | .NET 7 | Compile-time null-terminated literals + stack-allocated marshalling. |
| `Utf8.TryWrite` & `IUtf8SpanFormattable` | .NET 8 | Direct formatting into UTF-8 `Span<byte>`. |
| `GetAlternateLookup<ReadOnlySpan<byte>>` & `allows ref struct` | .NET 9 | Zero-allocation UTF-8 span dictionary/set lookups. |
| `Utf8.IndexOfInvalidSubsequence` | **.NET 11** | Reports exact byte index of malformed UTF-8 sequences. |
| `System.IO.StringStream` | **.NET 11** | Zero-allocation `Stream` wrapper over `string` / `ReadOnlyMemory<char>`. |
| `Utf8JsonWriter.Reset(JsonWriterOptions)` | **.NET 11** | Reuse pooled UTF-8 JSON writers even when changing formatting options. |
| JIT constant-folding for `SequenceEqual` on UTF-8 literals | **.NET 11** | Eliminates runtime comparisons when both spans are compile-time constants. |

---

## 5. Anti-Patterns to Avoid

* **Do not invent a custom `Utf8String` class:** Replacing `System.String` in domain models or ViewModels breaks compatibility with LINQ, serializers, reflection binders, logging frameworks, and UI controls.
* **Do not fight GUI framework encodings:** Avalonia, WPF, WinUI, MAUI, and `Terminal.Gui` require UTF-16 `string` for data binding and text layout. Convert lazily right at the UI boundary.
* **Do not reimplement BCL text utilities on byte spans:** Use built-in `System.IO.Path` and `[GeneratedRegex]` on `string` / `ReadOnlySpan<char>` rather than hand-rolling path normalization or regex logic over UTF-8 bytes.