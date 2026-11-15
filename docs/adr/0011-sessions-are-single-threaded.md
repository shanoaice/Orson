# Sessions are single-threaded, process-exclusive, and boundary-synchronized

---
status: accepted
---

libalpm does not provide thread safety.
A session, its databases, transactions, and package views belong to one thread at a time.
Furthermore, libalpm causes process-wide side effects and mutates internal state during reads.
Multiple libalpm handles cannot safely coexist in the same operating system process.
This document defines three architectural rules:
1. Document the process-wide pollution caused by libalpm.
2. Document lazy-load state mutations in native getters.
3. Enforce the exclusive process lease (`AlpmSession.Acquire`) and native boundary synchronization (`AlpmExecutionLock`).

## 1. Upstream Process Pollution (Process-Wide Unsafe State)

libalpm mutates global state in the host process.
These mutations affect all threads in the process:

- **GPGME static keyring anchoring**:
  In `signing.c`, `init_gpgme` initializes GPGME once using a static variable (`static int init = 0`).
  `gpgme_set_engine_info` binds the OpenPGP engine home directory (`sigdir`) to `handle->gpgdir`.
  GPGME never updates this home directory again.
  Subsequent handles or changed keyring paths continue to use the first configured directory.

- **Process working directory and umask mutations**:
  In `add.c`, `dload.c`, and `util.c`, libalpm invokes `chdir(handle->root)` and `chdir(localpath)`.
  In `be_local.c`, `be_sync.c`, and `util.c`, libalpm calls `umask(0022)` and `umask(0000)`.
  In POSIX processes, working directory and umask are process-wide attributes.
  These calls immediately affect relative file paths and file creation masks across all threads.

- **Process signal handler modifications**:
  In `dload.c`, libalpm overrides signal handlers for `SIGINT` and `SIGQUIT` with `sigaction`.
  In `util.c`, `_alpm_reset_signals` resets signal handlers for the whole process.
  GPGME also registers a process-wide handler for `SIGPIPE`.

- **Syslog closure (`closelog`)**:
  In `handle.c`, `_alpm_handle_free` calls `closelog()`.
  This call closes the system log file descriptor for the entire process.

- **Global libcurl initialization and cleanup**:
  In `alpm.c`, `alpm_initialize` calls `curl_global_init(CURL_GLOBAL_ALL)`.
  In `handle.c`, `_alpm_handle_free` calls `curl_global_cleanup()`.
  Freeing any handle shuts down libcurl process-wide.
  This shutdown corrupts any other active handle or concurrent HTTP client using libcurl.

## 2. Upstream Getter Lazy-Load State Mutations (Reads are Writes)

In libalpm, read-only operations perform hidden writes:

- **`be_local.c` `local_db_read` under `LAZY_LOAD`**:
  Local package property getters do not perform simple memory reads.
  Accessors such as `_cache_get_base`, `_cache_get_desc`, and `_cache_get_files` invoke `LAZY_LOAD(info)`.
  If the info flag is missing, `local_db_read` loads metadata from disk.
  `local_db_read` populates package fields and mutates `pkg->infolevel` without locking.
  Concurrent reads on the same package corrupt memory.

- **`db.c` `load_pkgcache`**:
  `_alpm_db_get_pkgcache_hash` checks whether the package cache is loaded.
  If the cache is missing, it calls `load_pkgcache(db)`.
  `load_pkgcache` frees any existing cache and re-populates the package hash table from disk.
  Concurrent package queries cause races during cache allocation and deallocation.

- **`package.c` `pm_errno` clobbering**:
  Every package getter in `package.c` resets the handle error state.
  Functions like `alpm_pkg_get_name` and `alpm_pkg_get_version` run `pkg->handle->pm_errno = ALPM_ERR_OK`.
  A concurrent read on another thread overwrites native error codes before the caller inspects them.

## 3. The Exclusive Process Lease (`AlpmSession.Acquire`)

Multiple concurrent sessions cannot operate safely within one process.
Therefore, `AlpmSession` constructors are internal.
Callers must acquire the session through `AlpmSession.Acquire(root, dbpath)`.

`AlpmSession.Acquire` enforces an exclusive process lease:
- It uses `Interlocked.CompareExchange(ref s_activeLease, 1, 0) == 0`.
- Only one live session can exist at any time.
- If a caller requests a second session while a lease is active, `Acquire` throws `InvalidOperationException`.
- When the session is disposed, `AlpmSession.ReleaseProcessLeaseInternal()` resets `s_activeLease` to `0`.
- Stale instances cannot release a lease owned by a newer session epoch.

## 4. Boundary Synchronization (`AlpmExecutionLock`)

Thread safety requires mutual exclusion at native boundaries.
Finalizers and background cleanup can run concurrently with user code.
`SafeAlpmHandle.ReleaseHandle()` runs on the GC finalizer thread or a thread pool worker.

`AlpmExecutionLock` provides process-wide synchronization for native calls:
- It wraps `System.Threading.Lock`.
- It exposes `Enter()`, `Exit()`, and `Hold()`.
- `Hold()` returns a `readonly ref struct Scope` for `using` statements.
- `AlpmSession.TryInitializeHandle` holds `AlpmExecutionLock.Hold()` during initialization.
- `AlpmSession.Dispose(bool disposing)` holds `AlpmExecutionLock.Hold()` during teardown.
- `SafeAlpmHandle.ReleaseHandle()` holds `AlpmExecutionLock.Hold()` during native destruction.

This lock prevents asynchronous finalization while native initialization or calls execute.

## 5. Thread Discipline for Consumers

The public API surface enforces strict thread ownership:
- A session, its databases, and its transactions belong to the acquiring thread.
- We do not add fine-grained locks around regular property getters.
- Callers must copy data using `Package.ToSnapshot()` before passing objects to other threads.
- Native callbacks (progress, log, event, question) execute on libalpm's thread.
- Callbacks must not block indefinitely.
- Question callbacks must return an answer before the native callback exits.
- Asynchronous answers in question callbacks cause undefined behavior in libalpm.

## Consequences

- The process lease prevents multiple handles from corrupting process-wide libraries (GPGME, libcurl, syslog).
- `AlpmExecutionLock` prevents races between native calls and garbage collection finalizers.
- Callers cannot construct uncoordinated concurrent sessions.
- Getters remain fast because normal member access avoids lock contention.
- Crossing thread boundaries requires explicit snapshots via `Package.ToSnapshot()`.

## See also

- [ADR 0001: F# interop boundary and Try seam](./0001-fsharp-interop-boundary-and-try-seam.md)
- [ADR 0006: Ship no escape hatch into the binding layer](./0006-ship-no-escape-hatch-into-the-binding-layer.md)
- [ADR 0007: Native errno capture, numeric typing, and public exception mapping](./0007-native-errno-and-exception-contract.md)
- [ADR 0008: Cleanup failures are exposed, not thrown](./0008-cleanup-failures-are-exposed-not-thrown.md)
- [ADR 0010: Enumerators revalidate the lifetime token](./0010-enumerators-revalidate-the-lifetime-token.md)
