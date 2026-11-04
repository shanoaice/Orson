# Callback Integration Validation TODO

## Status

Pending. Run this validation after Phases 0 through 5 are implemented and the isolated
transaction harness can execute a full 500-package simulation.

## Required Environment

- An isolated writable ALPM root and database.
- A generated package set large enough for 500 install operations.
- Callback handlers that record invocation counts without retaining borrowed spans or views.
- BenchmarkDotNet access to count allocations and Gen-0 collections during the transaction.

## Acceptance Cases

- Confirm every package operation emits the expected event sequence.
- Confirm repeated Progress callbacks allocate one package-name string per package, not one per
  percentage update.
- Confirm repeated Download callbacks allocate one filename string per file and no event objects.
- Confirm filtered Debug and Function logs do not format their `va_list`.
- Confirm accepted logs produce no managed payload allocation when the handler writes directly to
  a stream.
- Confirm retained `PackageView` values from an event are retired when the callback returns.
- Confirm no callback executes after session disposal.

## F# Adapter Design

- The synchronous F# CLI path may consume `ReadOnlySpan<byte>` directly in a span-aware stream.
- The F# actor and UI path must copy the log bytes inside the callback before posting to a
  `Channel`, `MailboxProcessor`, or task.
- Do not place a span in an F# record, discriminated-union case, option, or asynchronous state
  machine.
- Validate both adapter paths:
  - Stream sink: zero managed message allocation.
  - Actor sink: exactly one owned copy per accepted log message.
