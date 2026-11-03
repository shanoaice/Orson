# Cleanup failures are exposed, never thrown from Dispose

---
status: accepted
---

`Transaction.Dispose` currently ignores a failed `alpm_trans_release`: no exception, no record,
`_released` stays false and `CurrentTransaction` stays set. A later `alpm_release` then fails with
`ALPM_ERR_TRANS_NOT_NULL`, so the native handle is never freed and the callback context's GCHandle slot
leaks for the process lifetime — while the caller is told the transaction succeeded. We keep `Dispose`
non-throwing, because throwing from `finally` and `using` turns a cleanup problem into an unrelated
crash, and record the outcome on the transaction instead (`IsReleased`, and the failure itself), for
the F# `withPrepared` bracket to inspect in its `finally` and report as a distinct cleanup failure.

These reports do not go through the consumer's log callback. That callback is an inbound channel from
libalpm, owned and registered by the consumer; synthesizing the wrapper's own diagnostics into it
conflates two sources, it may never have been registered, and teardown is when it is least safe —
`AlpmCallbacks.Dispose()` is already skipped once handle release has failed.

**Consequences.** `Dispose` stays silent by contract, so "did the lock actually get released?" becomes
an explicit question the caller (or bracket) has to ask rather than something inferred from the
absence of an exception.

## Amendment: In-flight exception protection and fail-fast teardown

The guideline to avoid throwing from `Dispose()` exists primarily to prevent masking in-flight exceptions.
In .NET, throwing out of `finally` or `using` causes the CLR to discard any active exception during stack unwinding.
This loses the root-cause diagnostic information.

However, suppressing cleanup failures unconditionally hides fatal state corruption when no prior exception exists.
A disposal failure must distinguish between unwinding and clean completion:

1. **When prior execution may have faulted or aborted (uncommitted, in-progress):**
   An exception may already be in flight.
   `Dispose()` must suppress teardown exceptions to preserve the primary error.
   The failure must be recorded on the instance (such as `ReleaseFailure`) for inspection.

2. **When the operation completed cleanly (such as after `Commit()`):**
   No exception is in flight to mask.
   A cleanup failure represents fatal unmanaged state corruption (such as a leaked database lock).
   In this state, `Dispose()` throws to fail fast rather than allowing the application to continue in a damaged state.

## See also

- [ADR 0001: F# interop boundary and Try seam](./0001-fsharp-interop-boundary-and-try-seam.md)
- [ADR 0007: Native errno capture and exception mapping](./0007-native-errno-and-exception-contract.md)
