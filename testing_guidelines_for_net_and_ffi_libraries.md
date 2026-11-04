# Guidelines & Recommendations: Testing .NET & FFI Libraries

When authoring and maintaining reusable libraries—especially those interfacing with native binaries through P/Invoke or
Foreign Function Interfaces (FFI)—testing strategies typically benefit from moving beyond traditional unit test mocking.
This reference guide collects suggested patterns, structural recommendations, and reading material to help guide your
testing architecture.

---

## 1. Public API Surface & Behavioral Testing

Because library consumers interact solely through public entry points, testing behavioral contracts rather than internal
wiring often yields the most durable test suites.

* **Test public contracts directly:** Where feasible, consider exercising methods through the public API rather than
  exposing internals with `[InternalsVisibleTo]`. This helps ensure that refactorings don't unnecessarily break existing
  unit tests.
* **Leverage parameterized test cases:** Consider using data-driven tests (e.g., `[Theory]` and `[InlineData]` or
  `[MemberData]` in xUnit) to run edge cases through marshaling pipelines, especially for boundary values, empty
  buffers, null pointers, and atypical UTF strings.
* **Monitor API drift:** You might consider incorporating automated package validation tools (such as Microsoft's
  `Microsoft.DotNet.ApiCompat.Tool`) into your continuous integration workflow. This helps catch accidental breaking
  changes or unintentional shifts in public signatures between releases.

---

## 2. ABI & Memory Layout Verification

A frequent source of native crashes stems from subtle discrepancies between managed and unmanaged data structures.
Adding compile-time or unit-level structural checks can prevent subtle runtime memory corruption.

* **Assert struct sizes and offsets:** It is often worth writing lightweight unit tests that verify that
  `Marshal.SizeOf<T>()` and `Marshal.OffsetOf<T>()` match the expected C/native struct definitions.
* **Explicit layout specification:** When defining structs passed across native boundaries, consider explicitly
  declaring `[StructLayout(LayoutKind.Sequential)]` or `[StructLayout(LayoutKind.Explicit)]` alongside explicit packing
  values (`Pack = ...`) to prevent architecture-dependent padding surprises.
* **Custom marshaller validation:** If you use .NET 7+ source-generated P/Invokes (`[LibraryImport]`), custom
  marshallers (`CustomMarshallerAttribute`) can be tested as standalone pure classes. Verifying conversion logic in
  isolation before passing pointers to native functions can save significant debugging time.

```csharp
[Fact]
public void NativeHeader_StructLayout_MatchesExpectedAbi()
{
    // Verifying 64-bit alignment and layout expectations
    Assert.Equal(24, Marshal.SizeOf<NativeMessageHeader>());
    Assert.Equal(0, Marshal.OffsetOf<NativeMessageHeader>(nameof(NativeMessageHeader.Id)).ToInt32());
    Assert.Equal(8, Marshal.OffsetOf<NativeMessageHeader>(nameof(NativeMessageHeader.BufferPtr)).ToInt32());
}
```

---

## 3. Lifetime, Garbage Collection & Resource Safety

Interoperability bugs often manifest as intermittent access violations caused by the managed Garbage Collector
relocating or freeing references while native code is still working with them.

* **Delegate rooting during native callbacks:** When passing managed methods to native code as function pointers,
  consider validating that your wrapper types hold a persistent reference to the delegate (or use `GCHandle.Alloc`)
  until the unmanaged code signals completion. Calling `GC.KeepAlive(delegateInstance)` after invocation can help
  prevent early collection.
* **Buffer pinning validation:** For functions accepting memory spans or pointers, consider writing tests verifying that
  buffers remain pinned throughout the lifecycle of the call, avoiding undefined pointer redirection.
* **SafeHandle and finalization stress tests:** You may want to write lifecycle tests that intentionally trigger
  aggressive garbage collections (`GC.Collect()`, `GC.WaitForPendingFinalizers()`) across loops of allocating and
  releasing wrapper objects. This can surface double-free scenarios or leaks in your `SafeHandle` or `IDisposable`
  implementations early.

---

## 4. Test Isolation & Mocking Native Layers

While running against genuine native binaries provides the highest confidence, decoupling the managed wrappers from
native dependencies during early test stages can speed up local development.

* **Separate pure logic from interop calls:** You might consider designing your library with a thin abstraction boundary
  separating domain logic from raw native invocations. This allows fast, host-independent unit tests for business logic
  while reserving native calls for integration suites.
* **Dynamic library resolution in tests:** Consider using `NativeLibrary.SetDllImportResolver` in test fixtures to
  dynamically intercept and point library loads to mock dynamic libraries (`.dll`, `.so`, `.dylib`) or custom test
  fixtures built specifically for the test run.
* **Build a dedicated native test shim:** When testing complex native libraries, it is often helpful to compile a
  companion C/C++ test shared library that simulates native callbacks, error code returns, and fault injection without
  requiring the full production native engine.

---

## 5. Cross-Platform & Architecture Matrices

FFI code is inherently tied to the host platform's runtime environment, making multi-platform validation particularly
valuable.

* **Matrix builds:** If your library targets multiple platforms, running continuous integration tests across the primary
  supported targets (such as Linux, macOS, and Windows on both x64 and ARM64) helps catch platform-specific differences
  in pointer sizing, calling conventions, and dynamic linker paths.
* **Runtimes and run-paths:** Consider verifying that native dependencies are resolved correctly when deployed as
  standard NuGet packages via platform-specific runtime folders (`runtimes/{rid}/native/`).

---

## 6. Curated Reference Articles & Resources

Here are several recommended references to explore when refining your testing architecture:

### Official .NET Interop & Testing Documentation

*
**[Best Practices for Writing Unit Tests in .NET](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices)**  
*Focus:* Structuring clear test naming conventions, avoiding test duplication, and testing behavioral contracts.
*
**[Native Interoperability Best Practices (.NET)](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/best-practices)**  
*Focus:* Struct alignment, string marshaling strategies, memory management, and lifetime safety across native
boundaries.
* **[Source-Generated P/Invokes (
  `LibraryImport`)](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation)**  
  *Focus:* Transitioning to compile-time source generation and writing isolated custom marshallers.
* **[.NET Library Guidance](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/)**  
  *Focus:* Architectural guidance on backward compatibility, multi-targeting, and package layout.

### Practical Guides & FFI Testing Patterns

*
**[Unit Testing Best Practices in .NET (Milan Jovanović)](https://milanjovanovic.tech/blog/unit-testing-best-practices-dotnet)**  
*Focus:* Organizing practical test suites, structuring test data builders, and modern assertion patterns.
* **[P/Invoke Tips & Pitfalls (Ben Bowen)](https://benbowen.blog/post/pinvoke_tips/)**  
  *Focus:* Common edge cases when crossing managed and unmanaged boundaries, including boolean representation, struct
  packing, and calling convention discrepancies.
* **[Automated Testing of FFI Bindings (`ctest` & `bindgen` design patterns)](https://github.com/gnzlbg/ctest)**  
  *Focus:* A cross-language reference model demonstrating how to automatically generate and verify `sizeof`, `offsetof`,
  and type compatibility between foreign ABI definitions.
