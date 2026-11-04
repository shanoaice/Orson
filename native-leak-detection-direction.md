# Direction: Native Leak Detection

Date: 2026-11-17

## Decision

Orson.Alpm will make three related changes:

1. Resolve `CFreeExtern` through `NativeLibrary.GetMainProgramHandle()` instead of default handle-specific libc lookup.
2. Remove leak detection based on glibc `mallinfo2`.
3. Add differential, allocation-site-specific leak detection based on heaptrack.

CI scheduling, retention, and platform rollout are outside this report's scope.

## Rationale

The current `CFreeExtern` path resolves `free` through a concrete libc handle. This lookup can bypass a preloaded allocator or heaptrack wrapper.

The local experiments showed two consequences:

- With heaptrack, Clean incorrectly reported `1.01M / 72,000` leaked `__strdup` allocations. The allocations had already been freed.
- With mimalloc, the direct libc `free` path caused heap corruption. Orson Clean exited with code 139.

Global resolution through the main program handle fixed both problems in the corresponding probes. Clean no longer produced the false heaptrack leak. The real `Leak_InnerStrings` leak remained visible.

`mallinfo2` also has unsuitable semantics for these tests. It reports process-wide glibc state. Concurrent runtime activity can obscure the target delta. Its counters do not describe a mimalloc heap. In the mimalloc Inner probe, both `Uordblks` and `Hblkhd` remained zero despite a real leak.

Heaptrack provides allocation sites and call-count scaling. This evidence identifies the missing ownership operation more directly than a process-wide byte delta.

## 1. Change `CFreeExtern` Resolution

Update `src/Orson.Alpm/Interop/UnmanagedDestructors.cs` and its initialization path.

- Stop using default handle-specific libc resolution for `CFreeExtern`.
- Resolve the process-global `free` through `NativeLibrary.GetMainProgramHandle()`.
- Prefer one assembly-level `DllImportResolver` for `Orson.Alpm`.
- Register the resolver before the first relevant P/Invoke.
- Do not perform a native symbol lookup on every deallocation.
- Preserve the Cdecl callback ABI required by `alpm_list_free_inner`.

All `CFreeExtern` callers must use this resolution path. Do not retain a second direct-libc-free implementation.

This change applies to allocations whose contract requires the process allocator's ordinary `free`. APIs with private allocators or dedicated destructors must keep their existing release contracts.

### Acceptance

- Clean list ownership produces no target leak under heaptrack.
- A deliberately omitted string release still produces the expected string-allocation leak.
- The mimalloc Clean probe exits normally.
- Existing ownership and callback ABI tests continue to pass.

## 2. Remove `mallinfo2` Detection

Remove the heap-growth assertions and their supporting infrastructure. Relevant current locations include:

- `tests/Orson.Alpm.Tests/Unit/Ownership/ListOwnershipTests.cs`
- `tests/Orson.Alpm.Tests/Fixtures/NativeHeap.cs`
- `tests/Orson.Alpm.Tests/Unit/Ownership/ProcessWideNativeHeapCollection.cs`
- `tests/Orson.Alpm.Tests/Unit/Interop/TransientAlpmListBuilderTest.cs`

Remove obsolete `MallInfo2` declarations, `NativeHeapInUse` helpers, byte thresholds, and commented counter-based checks.

Delete `NativeHeap` after all consumers have migrated. Remove `ProcessWideNativeHeapCollection` if it only serializes `mallinfo2` tests. Preserve and rename it only if another test still requires its serialization semantics.

Do not replace these checks with another process-wide allocator counter. Ordinary functional ownership assertions remain necessary, but they do not replace leak detection.

## 3. Add Heaptrack Detection

Add a maintained heaptrack probe based on the validated experiment design.

Use two runs with identical initialization and warm-up. Use a low iteration count `N1` and a high iteration count `N2`. Analyze `N2 - N1` with `heaptrack_print --diff` and `--print-leaks`.

Assert target allocation-site signatures and their iteration scaling. Do not assert the process-wide `total memory leaked` value. CoreCLR and native runtime allocations make that total unsuitable as a verdict.

Cover these scenarios:

| Scenario | Required result |
|---|---|
| `Control_Clean` | No target allocation-site leak |
| `Leak_InnerStrings` | Detect unreleased string payloads |
| `Leak_ListNodes` | Detect unreleased `alpm_list_add` nodes |
| `Leak_MmapLargeChunk` | Detect unreleased transient builder storage |
| `Leak_RareOrSmall` | Detect the small injected leak independently of runtime totals |

Keep clean and faulty cases in the maintained test assets. The faulty cases must differ only in the omitted release operation. Sound cases must use the production ownership path.

Pin the supported heaptrack version for result parsing. Treat malformed or unsupported output as an invalid result. Save the native report needed to diagnose a failure. Full managed-stack symbolization is not required when native allocation sites provide stable signatures.

## Known Limits

- Heaptrack does not currently wrap `strdup` or `strndup`. When mimalloc supplies these functions, heaptrack can miss their internal allocations. The local mimalloc Inner probe demonstrated this gap.
- Heaptrack does not replace checks for use-after-free, invalid access, or double-free.
- Native allocation leaves may symbolize without useful C# method names.

These limits define the coverage boundary. They must not be hidden by suppressing an allocation-site family.

## Supporting Reports

- `reports/heaptrack-dlsym-interposition.md`: symbol resolution, mimalloc safety, and local resolver experiments.
- `reports/heaptrack-native-leak-testing.md`: heaptrack semantics and output-processing constraints.
