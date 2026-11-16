# Callback hot paths use scoped values and UTF-8 spans

---
status: accepted
---

High-frequency libalpm callbacks must avoid per-invocation managed allocations. The public callback
surface therefore distinguishes reusable stateless events, callback-scoped borrowed data, and
independent payload snapshots.

## Decisions

- Stateless `AlpmEvent` subclasses are canonical singletons. They contain no native state and may
  safely escape the callback.
- Event callbacks reuse one child lifetime domain. Returning from the callback advances its
  generation and retires every `PackageView` created during that frame. A nested event uses a
  separate child domain, so the inner frame cannot retire the outer frame early.
- `AlpmDownloadEvent` is a readonly value type with a `DownloadEventType` discriminator and
  strongly-typed record-struct payload variants (`DownloadInit`, `DownloadProgress`, `DownloadRetry`,
  `DownloadCompleted`). The value copies the native union and is safe to retain after the callback.
- Progress package names use a one-slot UTF-8 cache. Repeated callbacks for the same package reuse
  one managed string. Download filenames are decoded directly per event because parallel downloads
  interleave filenames and defeat single-slot caching.
- `LogHandler` receives callback-scoped `ReadOnlySpan<byte>` UTF-8 data. The callback formatter uses
  a 512-byte stack buffer and a pooled buffer for larger messages. The handler must copy the bytes
  if it needs to retain them.
- `EnabledLogLevels` defaults to `Error | Warning`. Filtering occurs before formatting the
  `va_list`.

## F# implications

F# can assign a lambda directly to the UTF-8 log delegate and can write the span to a synchronous
`Stream` without decoding. Synchronous CLI logging can therefore keep the zero-allocation path.

`ReadOnlySpan<byte>` cannot cross an asynchronous actor, channel, task, record, or discriminated
union boundary. An F# UI adapter must copy the message inside the synchronous callback, usually
with `messageUtf8.ToArray()`, and enqueue that immutable array. The adapter may decode once to a
string instead when its downstream consumer expects text.

The F# `EventSink` design therefore needs two paths:

- Synchronous sinks consume the borrowed span directly.
- Actor and UI sinks copy once before posting to their channel or mailbox.

`Console.Error` and other `TextWriter` values do not accept UTF-8 spans. Direct span output uses
`Console.OpenStandardError()`, `Console.OpenStandardOutput()`, or a `FileStream`.

## Consequences

- Ordinary event dispatch, repeated progress names, download event structs, and accepted logs have no
  steady-state managed payload allocation.
- Data-bearing `AlpmEvent` subclasses remain managed snapshots.
- Consumers must handle the callback-scoped log lifetime explicitly.
- The pre-1.0 download-event hierarchy and `Action<LogLevel, string>` log handler are replaced.

## See also

- [ADR 0010: Enumerators revalidate the lifetime token](./0010-enumerators-revalidate-the-lifetime-token.md)
- [ADR 0011: Sessions are single-threaded](./0011-sessions-are-single-threaded.md)
