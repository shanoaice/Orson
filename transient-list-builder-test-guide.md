# Transient ALPM List-Builder Test Guide

## Purpose and assumptions

This guide covers tests for:

- `TransientAlpmListBuilder`;
- `TransientAlpmStringListBuilder`;
- their stack-buffer optimization (SBO);
- multiple lists coexisting in one builder scope;
- native heap fallback and disposal.

The planned API direction is to accept `IReadOnlyCollection<T>` rather than general `IEnumerable<T>`. The test suite should therefore assume stable, materialized input collections. Do not add single-pass, streaming, or repeated-enumeration tests for the proposed API.

This is a test-writing exercise guide. It describes scenarios and assertions without providing test implementations.

## 1. Determining whether a pointer belongs to stack or heap storage

### 1.1 Use address-range membership

A pointer does not carry a portable stack or heap tag. Determine its origin by comparing its address with the address range of the scratch span supplied to the builder.

For these builders, the interpretation is:

- an address inside the supplied scratch range means stack-backed storage;
- a non-null address outside that range means the builder used its native heap fallback;
- a null pointer represents an empty list and has no storage location.

The second statement relies on the builder contract. In general, an outside address only proves that the pointer does not belong to that particular scratch span.

Use half-open ranges: include the start address and exclude the end address. The end pointer itself is valid for comparison but must not be dereferenced.

### 1.2 Preserve the original spans in the test

Declare the stack-allocated spans in the test method and retain those local variables for the assertions.

Passing a span to the builder copies the span value. The builder slices its private fields as it consumes capacity; this does not resize or reslice the test's original local span. The original span therefore still describes the complete scratch arena.

Use non-empty scratch spans for placement tests. Obtaining the first element's address is not meaningful for an empty span. A zero-capacity span can still be used when every non-empty result is expected to use the heap, but there is then no stack range to inspect.

### 1.3 Obtain the scratch start address

Two existing patterns are suitable:

- use `Unsafe.AsPointer` with a reference to the first span element, as the builders do;
- use a `fixed` block to obtain a pointer to the span, following `Utf8BufferTests.StringAtAndAcrossScratchBoundary_RoundTripsUtf8AndNulTerminates`.

For these tests, the spans refer to `stackalloc` memory and remain within the method scope. Keep all pointer assertions inside that scope.

Capture the start address before constructing lists. Also retain the original span length so later builder operations cannot affect the range calculation.

### 1.4 Check a list-node pointer

For `Span<_alpm_list_t>`:

1. Obtain a typed pointer to the first scratch element.
2. Compute the end with typed pointer arithmetic using the original element count.
3. Compare the returned `_alpm_list_t*` with that range.

Typed pointer arithmetic is preferable here: adding the span length already accounts for `sizeof(_alpm_list_t)`. This avoids mixing element counts and byte counts.

If raw integer addresses are used instead, prefer `nuint` for address comparisons. When calculating the byte size of the list arena, multiply its element count by the node size.

For additional diagnostic detail, compare each traversed node pointer with the same range. A correct list occupies one contiguous allocation, so checking its head is enough for the basic placement assertion.

### 1.5 Check a string payload pointer

The string builder has two independent storage decisions:

- the returned list pointer refers to `_alpm_list_t` nodes;
- each node's `data` pointer refers to that string's UTF-8 bytes.

Check them separately:

1. Compare the list pointer, and optionally every traversed node pointer, with the `Span<_alpm_list_t>` range.
2. Convert each `data` pointer to a byte pointer or raw address.
3. Compare that address with the original `Span<byte>` range.
4. Use the byte span's length directly; no node-size multiplication is involved.

Check every string's `data` pointer in placement tests. Checking only the first payload would not detect a later string incorrectly pointing outside the selected buffer.

A heap list can have stack-backed strings, and a stack list can have heap-backed strings. Tests must not infer one location from the other.

### 1.6 Keep pointer checks safe

- Check for null before classifying a returned list.
- Compare the head's `prev` pointer with the known tail before dereferencing that back-link.
- Do not dereference the one-past-the-end arena pointer.
- Do not inspect the private heap-allocation header; it is an implementation detail.
- Do not assert exact heap addresses, allocation alignment, or allocator block sizes.
- Complete all pointer use before the stack span leaves scope and before native allocations are disposed.

Address-range assertions are appropriate here because stack versus heap selection is the behavior under test. They should not become a general pattern for unrelated native-interop tests.

## 2. Assessment of the existing generic tests

The current tests in `ListOwnershipTests` provide useful happy-path coverage, but they do not establish the complete SBO and multi-list contract.

| Existing test | What it establishes | Important limitation |
| --- | --- | --- |
| `PreservesOrderAndTailBackLink` | Ordering and all links for one three-node stack list | Does not exercise subsequent construction |
| `MultipleListsRemainValidUntilScopeExit` | Three stack lists retain their payloads | Does not validate their links or any heap transition |
| `HeapFallback_SupportsLargeLists` | One large heap list has correct ordering and links | Only one heap allocation per builder |
| `RepeatedLargeScopesDoNotGrowNativeHeap` | Repeated disposal of one heap allocation does not show a large leak | Does not exercise several heap allocations in one builder scope |

The suite currently lacks:

- empty-input behavior;
- exact-capacity and one-over-capacity boundaries;
- stack-to-heap-to-residual-stack transitions;
- several simultaneous heap lists;
- full link validation for every coexisting list;
- explicit verification that the intended stack or heap path was used.

## 3. Recommended generic-builder additions

### 3.1 Strengthen multi-list validation

Extend the existing multi-list scenario so that, after all lists have been constructed, every list is checked for:

- payload order;
- every forward link;
- every intermediate back-link;
- null tail `next`;
- head `prev` identifying the tail.

Checking only payloads would miss corrupted links in the second and third lists.

### 3.2 Exercise mixed arena transitions

Use a small scratch arena and construct, in order:

1. a list that consumes part of the stack arena;
2. a list larger than the remainder, forcing heap fallback;
3. a list that fits the residual stack capacity;
4. another heap-backed list.

Assert the expected storage location for every list using the address-range technique.

After the final construction, revalidate every earlier list's payloads and links. This establishes that heap fallback neither consumes nor invalidates residual stack capacity, and that later lists do not overwrite earlier lists.

Small inputs are sufficient for this scenario. The existing 3,000-node test can remain as the large-input case.

### 3.3 Cover boundaries and singleton links

Add scenarios for:

- empty input returning null;
- empty input leaving the full stack arena available to a subsequent exact-fit list;
- exact remaining capacity using the stack;
- one element beyond remaining capacity using the heap;
- a singleton having null `next` and a head `prev` that references itself.

The empty-followed-by-exact-fit scenario proves non-consumption without inspecting private builder fields.

### 3.4 Exercise the heap disposal chain

Modify or supplement the native-heap cleanup scenario so that each measured builder scope constructs several oversized lists.

The current test allocates only one heap block per scope. Several blocks in one scope exercise the linked disposal chain and detect a leak of anything other than the most recent allocation.

Retain the existing repeated-scope approach and allocator-noise tolerance rather than asserting exact heap equality.

## 4. Recommended string-builder tests

### 4.1 Empty and singleton construction

Verify that:

- empty input returns null;
- a following exact-fit list can still consume the entire list-node scratch arena;
- a singleton contains the expected string;
- its `next` is null;
- its head `prev` references itself.

The singleton isolates the special head-link initialization from multi-node traversal.

### 4.2 Multiple strings and complete links

Construct several strings and validate:

- original ordering;
- every decoded payload;
- every forward link;
- every intermediate back-link;
- null tail `next`;
- head `prev` pointing to the final node.

The final condition is the priority regression assertion. The current implementation stores a pointer one node beyond the tail, so this test should fail until that defect is corrected.

Compare the head-back pointer with the already discovered tail before dereferencing it. This prevents the test itself from following an invalid back-link.

### 4.3 UTF-8 sizing, packing, and termination

Include ASCII, multibyte characters, and an emoji. Verify:

- every string round-trips from its native UTF-8 representation;
- every string has its trailing NUL;
- the next string starts after the preceding encoded bytes and terminator;
- scratch placement follows UTF-8 byte count rather than UTF-16 character count.

Decoding alone is insufficient. Also validate terminators and packing, because an incorrect offset can otherwise remain hidden behind apparently valid decoded values.

### 4.4 Independent node and byte arenas

Deliberately cover all four storage combinations:

| List nodes | UTF-8 bytes |
| --- | --- |
| Stack | Stack |
| Heap | Stack |
| Stack | Heap |
| Heap | Heap |

Include a later construction that reuses residual stack capacity after an earlier heap fallback.

For every constructed list, independently assert:

- node storage location;
- every string payload's storage location;
- payloads and all links.

Revalidate all lists after the final construction. This detects node overlap, byte-buffer overlap, accidental coupling between the two fallback decisions, and invalidation of earlier payloads.

### 4.5 Exact-fit boundaries

Test both dimensions independently:

- node count exactly equal to remaining list capacity;
- node count one greater than remaining list capacity;
- encoded byte count including terminators exactly equal to remaining byte capacity;
- one byte beyond remaining byte capacity.

Include multibyte input in the byte-boundary cases. ASCII-only inputs cannot distinguish character-count sizing from UTF-8 byte-count sizing.

Assert actual placement so that an incorrectly sized test cannot silently exercise the wrong branch.

### 4.6 Cleanup of both heap chains

In each measured builder scope, force:

- multiple list-node heap allocations;
- multiple string-buffer heap allocations.

Repeat the scope enough times to make a leaked earlier block visible above allocator noise.

This exercises both disposal chains. A test with only one list allocation and one buffer allocation per scope would not establish that earlier linked blocks are released.

## 5. Test-file organization

Use separate fixtures:

- `tests/Orson.Alpm.Tests/Unit/Interop/TransientAlpmListBuilderTests.cs`;
- `tests/Orson.Alpm.Tests/Unit/Interop/TransientAlpmStringListBuilderTests.cs`.

This mirrors the production types and follows the existing one-type-per-file pattern used by `Utf8BufferTests`.

Moving generic builder tests out of `ListOwnershipTests` improves cohesion. `ListOwnershipTests` primarily documents borrowed and owning list views; transient builders instead concern construction, scratch storage, and scoped native allocation.

### Helper ownership

- Give each fixture a focused helper for validating a complete native list.
- Keep the generic native-wrapper test type private to its fixture.
- Extract native-heap measurement helpers only when both fixtures need them.
- Avoid a shared base test class; unsafe span and pointer setup is clearer when local to each fixture.

### Native-heap test serialization

Any fixture that measures process-wide native heap state must remain in the `ProcessWideNativeHeap` collection. Preserve its disabled parallelization and existing allocator-noise approach.

Do not move heap-measurement tests into an ordinary parallel fixture. Concurrent native allocations could invalidate their measurements.

## 6. Avoid low-value or unspecified cases

Do not add tests for the following without first defining a public contract:

- null string elements;
- embedded NUL characters;
- concurrent mutation of input collections;
- repeated disposal;
- exact heap addresses or allocator block sizes;
- streaming, single-pass, or repeatedly changing enumerables under the planned `IReadOnlyCollection<T>` API.

These cases either exceed the intended contract or pin incidental implementation behavior.

## 7. Suggested exercise order

1. Extract the existing generic tests into their own fixture without changing behavior.
2. Add a complete-list assertion helper.
3. Strengthen the generic multi-list test.
4. Add generic placement, mixed-transition, and boundary scenarios.
5. Create the string fixture with empty, singleton, and multi-string cases.
6. Add the string head-to-tail regression assertion.
7. Add UTF-8 packing and independent arena-placement scenarios.
8. Extend cleanup coverage to several heap blocks per scope.

The string head-back-link test is the expected functional failure against the current implementation. Arena-placement tests should diagnose storage selection separately from that link defect.
