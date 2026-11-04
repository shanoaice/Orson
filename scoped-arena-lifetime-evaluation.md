# Architectural Evaluation: Applicability of Scoped Arena (Pattern 2) in Orson Lifetime Management

## 1. Executive Summary

The previous report ([`reports/native-lifetime-management-analysis.md`](file:///home/shanoaice/Projects/github/Orson/reports/native-lifetime-management-analysis.md)) evaluated four generic lifetime management patterns for unmanaged C bindings in garbage-collected runtimes.

In the [Orson](file:///home/shanoaice/Projects/github/Orson) codebase:
* **Pattern 1** is implemented for the root session handle via [`SafeAlpmHandle`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/SafeHandles.cs#L10).
* Child resources currently implement a **hybrid of Pattern 4 (Object Graph Mirroring) and a Distributed Generational Stamp**:
  * [`RootLifetime`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L121) mirrors the libalpm ownership hierarchy to [`ChildLifetime`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L211) and [`LifetimeFacet`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L189).
  * Every view ([`PackageView`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/PackageView.cs#L11), [`AlpmList<T>`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/List/AlpmList.cs#L35), [`Database`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Database.cs#L72)) holds a 32-byte [`LifetimeStamp`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L286) struct.
  * Every property read and enumerator step checks [`ThrowIfStale()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L311).

This report investigates the feasibility of introducing **Pattern 2 (Scoped Arena / Lifetime Context Allocation)** to reduce invasiveness and improve performance.

### Key Finding

Pattern 2 is **not** a drop-in replacement for long-lived database and package views. However, Pattern 2 is **exceptionally well-suited** for:
1. **Synchronous event and question callback frames** ([`AlpmCallbacks`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Events/AlpmCallbacks.cs#L127)).
2. **Transaction lifecycles** ([`Transaction`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Transaction.cs#L18)).
3. **FFI scratch buffer marshalling** ([`NativeListMarshal`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/NativeListMarshal.cs)).
4. **Scoped bulk cache enumerations** ([`AlpmList<T>`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/List/AlpmList.cs)).

---

## 2. Invasiveness and Overhead in the Current Architecture

The current implementation guarantees safety, but incurs measurable overhead:

```
[AlpmSession] (Pattern 1: SafeAlpmHandle)
      │
      ▼
[RootLifetime] ─────────────── holds _owner (keeps session alive)
      │
      ├──> [ChildLifetime: Database] ────> [LifetimeFacet: Servers]
      ├──> [ChildLifetime: Transaction]
      ├──> [ChildLifetime: LoadedPackage]
      └──> [ChildLifetime: CallbackFrame] (Allocated per event!)
            │
            ▼
      [LifetimeStamp] (32 bytes per view: 2 long generation counters + 2 references)
            │
            ├── PackageView.Name        ──> ThrowIfStale() check
            ├── PackageView.Version     ──> ThrowIfStale() check
            └── AlpmList.MoveNext()     ──> ThrowIfStale() check
```

### 2.1 Heap Churn in Callback Dispatch

During native events, [`AlpmCallbacks.EventAgent`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Events/AlpmCallbacks.cs#L144-L163) executes:

```csharp
callback._invokeDepth++;
var callbackLifetime = callback._rootLifetime.CreateChild("AlpmCallbacks.EventAgent");
try
{
  SafeInvoke(() => callback.EventHandler?.Invoke(AlpmEvent.FromUnion(eventT, callbackLifetime)),
    callback.HandlerException);
}
finally
{
  callback._invokeDepth--;
  callbackLifetime.Invalidate("callback frame ended");
}
```

* For every native event, a new [`ChildLifetime`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L211) managed object is allocated on the GC heap.
* Package downloads and transactions emit thousands of events.
* This creates high Gen-0 garbage collection churn for short-lived metadata wrappers.

### 2.2 Memory Footprint of `LifetimeStamp`

[`LifetimeStamp`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L286) contains:
* `Lifetime _resource` (8 bytes)
* `long _resourceGeneration` (8 bytes)
* `RootLifetime _root` (8 bytes)
* `long _rootGeneration` (8 bytes)

Total size is 32 bytes per stamp. Every [`PackageView`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/PackageView.cs#L11), [`FileList`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/FileList.cs#L20), [`AlpmList<T>`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/List/AlpmList.cs#L35), and [`AlpmQuestion`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Events/AlpmQuestion.cs#L21) carries this struct.

### 2.3 Traversal Check Penalties

As documented in [`LifetimeCheckBenchmarks.cs`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Benchmarks/LifetimeCheckBenchmarks.cs#L8-L18) and ADR 0010:
* Re-validating the lifetime stamp on every `AlpmList<T>.Enumerator.MoveNext` costs **~8.5 ns per element**.
* On bare advance loops, this adds a **2.89× slowdown**.
* On full package cache scans, every property read repeats the double-generation comparison.

---

## 3. Subsystem Evaluation for Pattern 2 (Scoped Arena)

Pattern 2 associates handle lifetimes with an execution scope or region. All resources allocated within the scope are bulk-invalidated or bulk-freed when the scope exits.

### 3.1 Subsystem Suitability Matrix

| Subsystem | Lifetime Characteristic | Suitability for Pattern 2 | Primary Benefit |
| :--- | :--- | :--- | :--- |
| **Session Root** ([`AlpmSession`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/AlpmSession.cs)) | Process-wide or long-lived | **No (Keep Pattern 1)** | Idiomatic `IDisposable` with GC finalizer fallback. |
| **Databases** ([`Database`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Database.cs)) | Dynamic, non-lexical lifecycle | **No (Keep Pattern 4 + Stamp)** | Prevents dangling references upon dynamic unregistration. |
| **Standalone Views** ([`PackageView`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/PackageView.cs)) | Escapes into consumer logic | **No (Keep Pattern 4 + Stamp)** | Consumers need to pass views across methods without scope restrictions. |
| **Callbacks & Questions** ([`AlpmCallbacks`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Events/AlpmCallbacks.cs)) | Synchronous call stack | **Ideal (Pattern 2)** | Zero GC allocation per event; compile-time escape prevention. |
| **Transactions** ([`Transaction`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Transaction.cs)) | Strictly bounded lexical scope | **Ideal (Pattern 2)** | Unified batch cleanup for packages and temporary lists. |
| **FFI Scratch Buffers** ([`NativeListMarshal`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/NativeListMarshal.cs)) | Transient invocation lifetime | **Ideal (Pattern 2)** | Replaces repetitive `malloc`/`free` with native bump allocation. |
| **Bulk Traversals** ([`AlpmList<T>`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/List/AlpmList.cs)) | Read-only scan loops | **High (Leased Scope)** | Bypasses per-element 8.5 ns check during traversal. |

---

## 4. Deep-Dive: High-Value Targets for Pattern 2

### 4.1 Target A: Callback and Question Frames

#### The Problem
`alpm_event_cb` and `alpm_question_cb` run synchronously on the session thread.
libalpm requires question answers before the callback returns.
Currently, [`AlpmCallbacks.EventAgent`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Events/AlpmCallbacks.cs#L152) allocates a new `ChildLifetime` instance for each callback invocation.

#### The Pattern 2 Solution
Use a stack-bound context or modern C# `ref struct`:

```csharp
public readonly ref struct AlpmQuestionContext
{
    private readonly unsafe _alpm_question_t* _raw;
    
    internal AlpmQuestionContext(unsafe _alpm_question_t* raw) => _raw = raw;

    // ref struct cannot be boxed, captured in async methods, or escaped to heap fields.
    public PackageView BorrowPackage() => new(_raw->install_ignorepkg.pkg, ...);
}
```

* **Zero GC Allocations**: No heap allocations for `ChildLifetime` or `AlpmQuestion` wrappers.
* **Compile-Time Safety**: The C# compiler enforces escape analysis. Callers cannot store the question view into a background thread or static field.
* **Preserves Snapshot Opt-In**: If callers need data after the callback returns, they call `Package.ToSnapshot()`.

---

### 4.2 Target B: Transaction Arena

#### The Problem
Transactions currently coordinate multiple tracking mechanisms:
* [`LoadedPackageRegistry`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/LoadedPackageRegistry.cs#L16) tracks file-loaded packages with a locked `HashSet<nint>`.
* When a package is handed to a transaction via `Transaction.AddPackage(pkg)`, [`LoadedPackage.Disown()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/LoadedPackage.cs#L54) manually detaches it.
* Unresolvable target lists and conflict lists must be individually reclaimed.

#### The Pattern 2 Solution
Treat [`Transaction`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Transaction.cs#L18) as an explicit Scoped Arena:

```
┌───────────────────────────────────────────────────────────┐
│ Transaction Arena Scope                                   │
│                                                           │
│  ├── Registered LoadedPackage Pointers                    │
│  ├── Added Target alpm_list_t Chains                      │
│  └── Temporary Unresolvable Package Lists                 │
└───────────────────────────────────────────────────────────┘
                             │
                      Dispose / Commit
                             │
                             ▼
     [Bulk Native Cleanup: All tracked resources freed]
     [Scope Closed: All child views atomically invalidated]
```

* Merges the responsibilities of `LoadedPackageRegistry` directly into the transaction scope.
* Exiting the `using (var tx = session.BeginTransaction())` block invalidates and frees all dependent items in batch.

---

### 4.3 Target C: Scratch Memory Arena for FFI Marshalling

#### The Problem
Converting collections to native `_alpm_list_t*` chains ([`NativeListMarshal`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/NativeListMarshal.cs)) requires individual `malloc` allocations for every node:

```csharp
// Currently allocates a native node per item
nativeList = NativeListMarshal.BuildPointerList(pkgList);
try { ... }
finally { NativeListMarshal.Free(nativeList); }
```

#### The Pattern 2 Solution
Introduce an unmanaged scratch arena:
* Allocates a contiguous block of memory per session or thread.
* Carves out `_alpm_list_t` nodes via a fast pointer bump.
* Resets the bump pointer to zero upon exiting the call, eliminating native allocator lock contention.

---

### 4.4 Target D: Leased Scope for Bulk Traversal

#### The Problem
In `AlpmList<T>`, iterating over 2,000+ packages in the local cache checks `ThrowIfStale()` on every `MoveNext` and property read. ADR 0010 accepted this cost (~8.5 ns per element) to prevent silent UAF reads if a concurrent thread disposes the session.

#### The Pattern 2 Solution
Introduce a scoped read lease:

```csharp
using (var lease = db.OpenReadScope())
{
    // Inside this scope, database and session modifications are blocked or verified once.
    // Enumerator skips per-element stamp re-validation.
    foreach (var pkg in lease.GetPackages())
    {
        Process(pkg.Name, pkg.Version);
    }
}
```

* Performs a single generational validation at scope entry.
* Replaces nanosecond check overhead across millions of loop iterations with zero-overhead raw pointer access within the lease.

---

## 5. Why Pattern 2 Cannot Completely Replace Pattern 4

Attempting to apply Pattern 2 universally across the entire API introduces two severe hazards:

1. **The Escaped Reference Hazard**:
   If `PackageView` required an active arena scope, standard idiomatic code would fail:
   ```csharp
   // Idiomatic C# pattern:
   public PackageView GetInstalledKernel() {
       return session.GetLocalDatabase().FindPackage("linux"); // Escapes scope!
   }
   ```
   If bound to a scope, returning `PackageView` outside a `using` block causes immediate `ObjectDisposedException` or memory corruption.
2. **API Ergonomics Degradation**:
   Requiring lambda-based scopes (`session.WithDatabase(db => ...)`) for simple read queries imposes heavy ceremony on library consumers.

Therefore, long-lived query objects (`Database`, `PackageView`) must retain their current **Pattern 4 + Generational Stamp** design.

---

## 6. Target Hybrid Architecture

| Layer | Component | Pattern | Implementation Mechanism |
| :--- | :--- | :--- | :--- |
| **Session Root** | [`AlpmSession`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/AlpmSession.cs) | **Pattern 1** | [`SafeAlpmHandle`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/SafeHandles.cs#L10) (`SafeHandleZeroOrMinusOneIsInvalid`). |
| **Long-Lived Objects** | [`Database`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Database.cs), [`PackageView`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/PackageView.cs) | **Pattern 4 + Stamp** | [`RootLifetime`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L121) $\rightarrow$ [`ChildLifetime`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L211) with [`LifetimeStamp`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L286). |
| **Callback Execution** | [`AlpmCallbacks`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Events/AlpmCallbacks.cs), [`AlpmQuestion`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Events/AlpmQuestion.cs) | **Pattern 2** | Stack-bound `ref struct` / reusable `CallbackScope`. |
| **Transaction State** | [`Transaction`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Transaction.cs) | **Pattern 2** | Scoped transaction arena with batch resource deregistration. |
| **FFI Marshalling** | [`NativeListMarshal`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/NativeListMarshal.cs) | **Pattern 2** | Bump-allocated unmanaged scratch arena. |
| **High-Throughput Scans** | [`AlpmList<T>`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/List/AlpmList.cs) | **Pattern 2 (Opt-In)** | Scoped read lease bypassing per-element validation. |

---

## 7. Conclusions & Implementation Steps

1. **Retain Pattern 1 and the Generational Model for Query APIs**:
   Keep `SafeAlpmHandle` and `LifetimeStamp` for `Database` and `PackageView` to preserve idiomatic C# ergonomics.
2. **Refactor Event Callbacks to Scoped Contexts**:
   Eliminate per-event `ChildLifetime` heap allocations in `AlpmCallbacks.EventAgent`. Replace with a reusable `CallbackScope` or `ref struct`.
3. **Consolidate Transaction Lifetime into an Arena**:
   Retire `LoadedPackageRegistry` and migrate file-loaded package tracking directly into a scoped transaction context.
4. **Benchmark Scratch Arena Marshalling**:
   Replace per-call `alpm_list_t` `malloc` allocations in `NativeListMarshal` with an arena-backed bump allocator. Verify using BenchmarkDotNet.
