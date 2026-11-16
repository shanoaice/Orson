# Orson.Alpm Async Worker & GUI/TUI Host Design Document

## 1. Problem Statement & Scope

`libalpm` is a synchronous, single-threaded C library where even "read-only" getters (such as reading package descriptions, dependencies, or file lists from the local database) lazily mutate internal C structs (`_alpm_local_db_read`) and `handle->pm_errno` without synchronization.

In CLI applications, executing everything synchronously on the main thread is natural. However, in **GUI (Avalonia, WPF, WinUI)** and **TUI (Terminal.Gui, Spectre.Console)** applications:
1. Running `libalpm` disk/network I/O on the UI thread freezes rendering and input.
2. Wrapping individual `AlpmSession` calls in naive `Task.Run` blocks schedules work across arbitrary ThreadPool threads, risking concurrent access violations or lazy `IEnumerable<PackageView>` enumeration escaping back to the UI thread.
3. Holding a naive `SemaphoreSlim(1, 1)` across a phased transaction (`prepare` $\to$ user confirmation dialog $\to$ `commit`) deadlocks the UI if the user clicks on a package in the confirmation dialog to inspect its metadata before confirming.

This document specifies the **`AlpmWorker`** (Single-Threaded Command-Loop Actor) architecture, designed to provide a deadlock-free, C#- and F#-idiomatic asynchronous host over `Orson.Alpm`.

---

## 2. Core Architectural Principles

1. **Single-Owner Turn Serialization:**
   An `AlpmWorker` owns an `AlpmSession` exclusively and processes work items sequentially via a bounded/unbounded `System.Threading.Channels.Channel<WorkItem>` on a dedicated background loop.
2. **Scoped Access (No Escaping Views):**
   `AlpmSession` and `PreparedTransaction` never escape the worker closure. Callers submit scoped work functions (`Func<AlpmSession, T>`) that query `PackageView`s on the worker thread and return materialized, thread-safe results (`T`) via `Task<T>`.
3. **Interactive Pause Multiplexing (Deadlock-Free Confirmation):**
   When a transaction finishes Phase 1 (`prepare`) and pauses for user confirmation (`"Proceed with installation? [Y/n]"`), the worker transitions into a **`PreparedPaused`** state. While paused waiting for the user's decision, the worker loop continues servicing **read-only inspection queries** (allowing the GUI user to click on and inspect packages in the upgrade list!) while rejecting conflicting mutation commands.
4. **Zero-Friction C# and F# Interop:**
   All public signatures on `AlpmWorker` and `IAsyncQuestionPrompter` use unified BCL primitives (`Task<T>`, `ValueTask<T>`, `CancellationToken`, `System.Func<...>`, `IReadOnlyList<T>`).
   - **C# consumers** use standard `async`/`await` and C# lambdas (`session => ...`).
   - **F# consumers** use `task { ... }` and F# lambdas (`fun session -> ...`), which the F# compiler implicitly converts to `System.Func<...>`.

---

## 3. State Machine & Deadlock-Free Phased Transactions

A major pitfall of naive mutex locking in GUI package managers occurs during the pause between `alpm_trans_prepare` and `alpm_trans_commit`:

```
[Idle State] (db.lck unlocked)
     │
     ├─► Accepts: QueryAsync (Read)
     ├─► Accepts: UpdateDatabasesAsync (Mutation)
     └─► Accepts: BeginTransaction / ExecutePlanAsync (Mutation)
           │
           ▼ (Runs alpm_trans_prepare on Worker Thread)
[PreparedPaused State] (db.lck HELD by active PreparedTransaction)
     │
     ├─► UI displays TransactionSummary ("84 packages to upgrade. Proceed?")
     │
     ├─► Accepts: QueryAsync / InspectPreparedAsync (Read-Only!)
     │     └─► UI user clicks "linux" in upgrade list to view dependencies/changelog.
     │         Worker executes read query on the SAME thread safely without deadlocking!
     │
     ├─► Rejects: Concurrent mutations (UpdateDatabases, second BeginTransaction)
     │
     ├─► User clicks "Cancel" ──► Worker runs Abort() / releases db.lck ──► [Idle State]
     │
     └─► User clicks "Confirm" ─► Worker runs alpm_trans_commit
                                  Releases db.lck in finally block ───────► [Idle State]
```

### Why This Beats Naive GUI Input Locking:
Instead of freezing or disabling the entire GUI inspection pane while the confirmation modal/view is open, the `PreparedPaused` state allows the user to browse package details, view changelogs, and inspect dependency trees of the staged transaction—serialized safely on the worker loop while `db.lck` remains held.

---

## 4. Public API Surface (C# & F# Unified)

### 4.1 The `AlpmWorker` Host

```csharp
namespace Orson.Alpm.FSharp // Or Orson.Alpm.Async

public sealed class AlpmWorker : IAsyncDisposable, IDisposable
{
    /// Starts the dedicated worker loop and initializes the underlying AlpmSession.
    public static Task<Result<AlpmWorker, SessionError>> StartAsync(
        AlpmSessionConfig config,
        EventSink? eventSink = null,
        CancellationToken ct = default);

    /// Enqueues a synchronous read/query closure onto the worker thread.
    /// If the user cancels `ct` before the work item starts, it is skipped without touching libalpm.
    public Task<TResult> QueryAsync<TResult>(
        Func<AlpmSession, TResult> query,
        CancellationToken ct = default);

    /// Updates sync databases on the worker thread.
    public Task<Result<bool, DatabaseError>> UpdateDatabasesAsync(
        bool force = false,
        CancellationToken ct = default);

    /// Executes a declarative TransactionPlan with an asynchronous confirmation pause
    /// between Prepare and Commit.
    public Task<Result<TransactionReport?, TransactionError>> ExecutePlanAsync(
        TransactionPlan plan,
        IAsyncQuestionPrompter prompter,
        Func<TransactionSummary, CancellationToken, Task<bool>> confirmBeforeCommit,
        CancellationToken ct = default);
}
```

### 4.2 Usage from C# (e.g., Avalonia / WPF ViewModel)
```csharp
// 1. Non-blocking search query with CancellationToken support (for search-box debouncing)
var searchResult = await _worker.QueryAsync(session =>
{
    var extra = Database.sync("extra", session).Value;
    return Database.search([searchText], extra);
}, cancellationToken);

// 2. Full phased transaction with async UI modal confirmation
var outcome = await _worker.ExecutePlanAsync(
    plan: myPlan,
    prompter: _avaloniaModalPrompter,
    confirmBeforeCommit: async (summary, ct) =>
    {
        // Runs on worker or marshals to UI thread; while awaiting user click,
        // the worker loop can still process _worker.QueryAsync(...) calls!
        return await ShowUpgradeConfirmationDialogAsync(summary, ct);
    },
    ct: cancellationToken);
```

### 4.3 Usage from F# (e.g., Elmish / Avalonia.FuncUI)
```fsharp
// F# automatically converts `fun session -> ...` into `Func<AlpmSession, 'T>`!
let searchPackages (worker: AlpmWorker) (query: string) (ct: CancellationToken) = task {
    return! worker.QueryAsync((fun session ->
        Database.allSync session
        |> List.collect (fun db ->
            match Database.search [query] db with
            | Ok views -> views |> List.map (Package.snapshot false)
            | Error _ -> [])
    ), ct)
}
```

---

## 5. Bridging Synchronous C Callbacks to Async UI (`IAsyncQuestionPrompter`)

### 5.1 The Threading Problem During Native Callbacks
Unlike the pause *between* `prepare` and `commit` (where C execution has returned to .NET and the worker loop can freely process its channel), **`alpm_cb_question` fires *while* the C stack frame is active inside `alpm_trans_prepare` or `alpm_trans_commit`.**

During `alpm_cb_question`:
1. The worker OS thread **must block** until the UI thread resolves the user's answer.
2. Because the UI thread (e.g., Thread #1) will render the question dialog while the worker thread (e.g., Thread #8) is blocked inside `alpm_cb_question`, **the UI thread must not call `PackageView` P/Invoke getters concurrently from Thread #1**.

### 5.2 The Solution: Snapshot-Backed Async Question Payloads
To make GUI question dialogs 100% thread-safe without requiring the UI thread to round-trip back to the blocked worker thread just to read `pkg.Name` or `pkg.Version`:
- The synchronous `IQuestionPrompter` (Section 3 of `DESIGN.md`) continues to use zero-copy `PackageView` for single-threaded CLI apps.
- The `AlpmWorker` adapter (`IAsyncQuestionPrompter`) converts the small number of candidate packages in a question (typically 1–4 packages) into lightweight `PackageSnapshot`s (`includeFiles = false`) **on the worker thread before dispatching the `Task<...>` to the UI thread**:

```csharp
public interface IAsyncQuestionPrompter
{
    Task<int?> SelectProviderAsync(AsyncProviderChoice choice, CancellationToken ct);
    Task<bool> ResolveConflictAsync(AsyncConflictChoice choice, CancellationToken ct);
    Task<bool> ApproveReplacementAsync(AsyncReplaceChoice choice, CancellationToken ct);
    Task<bool> SkipUnresolvableAsync(IReadOnlyList<PackageSnapshot> packages, CancellationToken ct);
    Task<bool> ImportKeyAsync(string keyUid, string fingerprint, CancellationToken ct);
    Task<bool> DeleteCorruptedAsync(string filePath, CancellationToken ct);
    Task<bool> InstallIgnoredAsync(PackageSnapshot package, CancellationToken ct);
}
```
Inside `AlpmWorker`, a small internal bridge implements the synchronous `IQuestionPrompter` by:
1. Snapshotting the 1–4 candidate `PackageView`s into `PackageSnapshot`s on the worker thread.
2. Invoking the corresponding `IAsyncQuestionPrompter.*Async(...)` method.
3. Blocking the dedicated ALPM worker thread via `.GetAwaiter().GetResult()` until the UI `Task` completes.
4. Writing the result back to `libalpm`.

Because the ALPM worker runs on its own dedicated background thread (not a UI thread), blocking the worker thread inside `.GetAwaiter().GetResult()` while awaiting the UI thread's `TaskCompletionSource` **cannot deadlock the UI thread**.

### 5.3 UTF-8 Log Adapter

`AlpmCallbacks.LogHandler` receives a `ReadOnlySpan<byte>` that is valid only during the synchronous callback:

- A synchronous CLI sink may write the span directly to `Console.OpenStandardOutput()`, `Console.OpenStandardError()`, or a `FileStream`.
- An actor or UI sink must copy the bytes before posting to a `Channel`, `MailboxProcessor`, or task. The adapter can enqueue `messageUtf8.ToArray()` or decode it once to a string.
- An F# record, discriminated-union case, option, or asynchronous state machine cannot contain the span.

The `EventSink` implementation therefore needs one direct span path and one owned-copy path. The owned-copy path preserves safety across the worker/UI boundary and intentionally pays one allocation per accepted log message.

---

## 6. Under-the-Hood Implementation Sketch (~80 Lines of F#)

The worker requires no external actor library—only `System.Threading.Channels` and a single background loop:

```fsharp
type private WorkerCommand =
    | ReadQuery of execute: (AlpmSession -> unit)
    | Mutation of execute: (AlpmSession -> Task)

// Inside AlpmWorker's dedicated loop:
// 1. In Idle state: reads from main `_commandChannel` (both ReadQuery and Mutation).
// 2. In PreparedPaused state (inside ExecutePlanAsync while awaiting `confirmBeforeCommit`):
//    WaitAny between `confirmTask` and `_readOnlyChannel.Reader.WaitToReadAsync()`!
//    Any `QueryAsync` call arriving while the confirmation dialog is open is serviced
//    immediately on the worker loop between `prepare` and `commit`!
```

Look at that `WaitAny` trick in Step 2 above:
- When `ExecutePlanAsync` finishes `alpm_trans_prepare`, it starts `let confirmTask = confirmBeforeCommit summary ct`.
- While `not confirmTask.IsCompleted`, the worker loop waits on **either** `confirmTask` completing **or** a `ReadQuery` arriving in `_readOnlyChannel`!
- If the GUI user clicks a package in the confirmation screen, `QueryAsync` pushes to `_readOnlyChannel`, the worker loop wakes up, executes `QueryAsync(session)` on the worker thread, and goes right back to awaiting `confirmTask`!
- Once the user clicks "Confirm" or "Cancel", `confirmTask` completes, the worker runs `alpm_trans_commit` (or `Abort`), disposes the transaction, and resumes processing the main `_commandChannel`.

---

## 7. Packaging & Rollout Strategy

1. **No 3rd Assembly Needed:**
   Include `AlpmWorker` and `IAsyncQuestionPrompter` directly inside `Orson.Alpm.FSharp` (under `namespace Orson.Alpm.Async` or `Orson.Alpm.FSharp`). Because its public methods use `Task<T>`, `Func<...>`, and `CancellationToken`, both C# and F# GUI/TUI projects can reference `Orson.Alpm.FSharp` and use `AlpmWorker` natively.
2. **Implementation Order:**
   - **Milestone 1:** Complete the C# core refactorings (`PackageView` struct, `LocalDatabase`/`SyncDatabase` split, `TryX` bridge, mutual-exclusion re-entrancy guard).
   - **Milestone 2:** Implement the core synchronous F# modules (`Database`, `Package`, `Transaction`, `PlanDsl`, `EventSink`).
   - **Milestone 3:** Add `AlpmWorker` (~100 lines of F#) on top of Milestone 2 as the turnkey async host for GUI/TUI consumers.
