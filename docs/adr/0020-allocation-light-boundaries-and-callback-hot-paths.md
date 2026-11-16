# Allocation-light boundaries and callback hot paths

---
status: accepted
---

Native FFI and callback boundaries must balance allocation overhead with code complexity.
Eliminating every managed allocation can introduce excessive state, synchronization, and maintenance debt.
This record defines the trade-off framework for allocation reduction across different call frequencies and consumer architectures.

## Trade-off framework

Weigh allocation reduction against code complexity.
Do not introduce complex caching without demonstrated GC pressure.

### Call frequency tiers

1. **Tight CPU loops**:
   - Operations like package extraction progress run in tight loops.
   - Repeated allocations here quickly trigger Gen 0 garbage collections.
   - These collections stop all managed threads.
   - Use strict zero-allocation techniques here: single-slot caches, readonly structs, and stack buffers.

2. **Bursty I/O and network operations**:
   - Operations like file downloads and logging occur at network or I/O speeds.
   - Even when multiple downloads run concurrently, allocations accumulate slowly.
   - Complex stateful caches (such as LRU tables) add maintenance debt and synchronization overhead.
   - Prefer simple direct decoding or standard buffer pooling over complex caches.

3. **Discrete events and user interactions**:
   - Operations like transaction state changes and question prompts occur infrequently.
   - These allocations have negligible impact on garbage collection.
   - Prioritize clean API ergonomics, type safety, and lifetime correctness over zero-allocation tricks.

### Consumer architecture realities

1. **CLI applications**:
   - Headless or streaming CLI tools can consume borrowed UTF-8 spans directly.
   - They avoid decoding overhead on log and output streams.

2. **TUI applications**:
   - Terminal refresh rates naturally throttle visual output.
   - Network arrival delays naturally mask minor garbage collection pauses during downloads.
   - Zero-allocation hot paths protect CPU-bound extraction loops from stutter.

3. **GUI applications**:
   - GUI frameworks dispatch updates from a background worker thread to a UI dispatcher.
   - Ref structs and borrowed spans cannot cross thread or asynchronous boundaries.
   - The UI layer must copy or decode data before dispatching.
   - Eliminating an allocation on the background thread provides no benefit if the UI layer must allocate immediately.

## Decisions

- Stateless `AlpmEvent` subclasses are canonical singletons. They contain no native state and may safely escape the callback.
- Event callbacks reuse one child lifetime domain. Returning from the callback advances its generation and retires every `PackageView` created during that frame. A nested event uses a separate child domain, so the inner frame cannot retire the outer frame early.
- `AlpmDownloadEvent` is a readonly value type with a `DownloadEventType` discriminator and strongly-typed record-struct payload variants (`DownloadInit`, `DownloadProgress`, `DownloadRetry`, `DownloadCompleted`). The value copies the native union and is safe to retain after the callback.
- Progress package names use a one-slot UTF-8 cache. Repeated callbacks for the same package reuse one managed string. Download filenames are decoded directly per event because parallel downloads interleave filenames and defeat single-slot caching.
- `LogHandler` receives callback-scoped `ReadOnlySpan<byte>` UTF-8 data. The callback formatter uses a 512-byte stack buffer and a pooled buffer for larger messages. The handler must copy the bytes if it needs to retain them.
- `EnabledLogLevels` defaults to `Error | Warning`. Filtering occurs before formatting the `va_list`.

## F# and UI implications

F# can assign a lambda directly to the UTF-8 log delegate.
It can write the span to a synchronous `Stream` without decoding.
Synchronous CLI logging can therefore keep the zero-allocation path.

`ReadOnlySpan<byte>` cannot cross an asynchronous actor, channel, task, record, or discriminated union boundary.
An F# or GUI adapter must copy the message inside the synchronous callback, usually with `messageUtf8.ToArray()`.
The adapter may decode once to a string instead when its downstream consumer expects text.

The event sink design therefore needs two paths:
- Synchronous sinks consume the borrowed span directly.
- Actor and UI sinks copy once before posting to their channel, mailbox, or dispatcher.

`Console.Error` and other `TextWriter` types do not accept UTF-8 spans.
Direct span output uses `Console.OpenStandardError()`, `Console.OpenStandardOutput()`, or a `FileStream`.

## Consequences

- High-frequency progress callbacks, download event payloads, and accepted logs achieve zero or minimal steady-state allocation.
- Code avoids fragile caching where allocation rates are naturally low or bursty.
- Consumers must handle callback-scoped log lifetimes explicitly.
- Cross-thread UI and actor boundaries retain explicit copy seams.

## See also

- [ADR 0010: Enumerators revalidate the lifetime token](./0010-enumerators-revalidate-the-lifetime-token.md)
- [ADR 0011: Sessions are single-threaded](./0011-sessions-are-single-threaded.md)
