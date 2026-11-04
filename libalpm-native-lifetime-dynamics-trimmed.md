# Architecture-Agnostic Lifetime and Ownership Dynamics of libalpm

## 1. Scope and Objective

This report specifies the native memory and lifetime contracts of the Arch Linux Package Management C library (`libalpm`).
It provides an objective baseline to evaluate binding architectures against native C memory rules.

`libalpm` relies on explicit C memory allocation (`malloc`, `free`, `strdup`).
It provides no reference counting and no garbage collection.
Every native object belongs to a strict hierarchy of ownership, mutability, and invalidation rules.

---

## 2. Core Native Object Taxonomy

`libalpm` organizes unmanaged state across nine primary native object categories:

```
                            [alpm_handle_t] (Root Session)
                                   │
         ┌─────────────────────────┼─────────────────────────┐
         ▼                         ▼                         ▼
   [alpm_db_t] (Local)       [alpm_db_t] (Sync)       [alpm_trans_t] (Transaction)
         │                         │                         │
         ├─> pkgcache              ├─> pkgcache              ├─> Added file packages
         ├─> grpcache              ├─> grpcache              └─> Error payload lists
         └─> (Permanent)           ├─> servers (Facet)
                                   └─> cache_servers (Facet)
                                   │
                                   ▼
                             [alpm_pkg_t] (Database Package)
                                   │
                                   ├─> alpm_filelist_t
                                   ├─> changelog stream
                                   └─> mtree stream
```

| Native Entity | Allocation API | Destruction API | Owner | Invalidation Trigger |
| :--- | :--- | :--- | :--- | :--- |
| `alpm_handle_t*` | `alpm_initialize` | `alpm_release` | Caller | Explicit release |
| `alpm_db_t*` (Local) | `alpm_initialize` | `alpm_release` | `alpm_handle_t` | `alpm_release` |
| `alpm_db_t*` (Sync) | `alpm_register_syncdb` | `alpm_db_unregister` | `alpm_handle_t` | Unregister, unregister-all, or session release |
| `alpm_db_t*` (Servers) | `alpm_db_add_server` / `set` | `alpm_db_remove_server` / `set` | `alpm_db_t` | Any server list mutation |
| `alpm_pkg_t*` (Database) | Lazy parsing into `pkgcache` | Internal `_alpm_pkg_free` | `alpm_db_t` | DB update, DB unregister, or transaction commit (local) |
| `alpm_pkg_t*` (Loaded) | `alpm_pkg_load` | `alpm_pkg_free` | Caller | Explicit free or transaction hand-over |
| `alpm_pkg_t*` (Transaction) | `alpm_add_pkg` | `alpm_trans_release` | `alpm_trans_t` | Transaction release |
| `alpm_trans_t` | `alpm_trans_init` | `alpm_trans_release` | `alpm_handle_t` | Transaction release |
| `alpm_filelist_t` | Part of package parsing | Internal package teardown | `alpm_pkg_t` | Parent package teardown |
| `alpm_list_t*` (Borrowed) | Internal library structures | Not freed by caller | Respective parent | Parent object mutation or teardown |
| `alpm_list_t*` (Owned) | Query/check functions | `alpm_list_free[_inner]` | Caller | Caller deterministic release |
| `alpm_event_t*` | Internal dispatch frame | Automatic stack drop | Native C stack | Callback function return |
| `alpm_question_t*` | Internal dispatch frame | Automatic stack drop | Native C stack | Callback function return |

---

## 3. Detailed Lifetime Dynamics by Entity

### 3.1 The Root Session (`alpm_handle_t*`)

#### Allocation and Teardown
* `alpm_initialize(root, dbpath, &err)` allocates the root handle and associated state.
* `alpm_release(handle)` frees the handle and its entire subordinate object graph.

#### Invariants and Constraints
1. **Thread Affinity:** `libalpm` is not thread-safe. All calls on a handle must be strictly serialized.
2. **Transaction Lockout:** `alpm_release` refuses to run while a transaction is active. It returns `-1` (`ALPM_ERR_TRANS_NOT_NULL`). The caller must release any active transaction before releasing the handle. Otherwise, the session leaks memory and leaves `db.lck` locked.
3. **Total Cascading Invalidation:** `alpm_release` frees all registered sync databases, the local database, option lists, and internal cache entries. Any pointer borrowed from this handle becomes dangling immediately.

---

### 3.2 Databases (`alpm_db_t*`)

`libalpm` implements two distinct database categories with divergent lifetime dynamics:

#### Local Database (`alpm_get_localdb`)
* The local database represents installed packages.
* It is created during `alpm_initialize` and owned by the handle.
* It cannot be unregistered. Calling `alpm_db_unregister` returns an error.
* **Cache Reallocation on Commit:** Calling `alpm_trans_commit` modifies the installed system. `libalpm` frees and re-reads the local database package cache (`pkgcache`). Every package pointer previously obtained from the local database becomes invalid upon commit.

#### Sync Databases (`alpm_register_syncdb`, `alpm_get_syncdbs`)
* Sync databases represent remote package repositories (e.g. `core`, `extra`).
* `alpm_register_syncdb` appends a new database node to `handle->dbs_sync`.
* `alpm_db_unregister(db)` frees the database struct, its server lists, and all cached packages.
* `alpm_unregister_all_syncdbs(handle)` frees every sync database and clears the list.
* **Cache Drop on Update:** `alpm_db_update(force, db)` downloads new repository archives. Updating frees the existing `pkgcache` and replaces it with fresh package nodes. All previous package views for that database become dangling.

#### Sub-resource Facets: Server Lists
* `alpm_db_get_servers(db)` and `alpm_db_get_cache_servers(db)` return internal `alpm_list_t*` pointers.
* Modifying servers (`alpm_db_add_server`, `alpm_db_remove_server`, `alpm_db_set_servers`) frees or reallocates the server list nodes in place.
* Server mutations invalidate existing server list pointers.
* Server mutations do **not** invalidate package caches within that database.

---

### 3.3 Packages (`alpm_pkg_t*`)

`libalpm` packages have three distinct origins with incompatible ownership contracts:

```
                  ┌───────────────────────────────────────────────┐
                  │               Package Origins                 │
                  └───────────────────────┬───────────────────────┘
                                          │
         ┌────────────────────────────────┼────────────────────────────────┐
         ▼                                ▼                                ▼
  [Database Package]            [File-Loaded Package]            [Transaction Package]
  (alpm_db_get_pkg)               (alpm_pkg_load)                  (alpm_add_pkg)
         │                                │                                │
         ├─ Owned by alpm_db_t            ├─ Owned by caller               ├─ Hand-over from load
         ├─ NEVER free directly           ├─ MUST free with alpm_pkg_free  ├─ Owned by transaction
         └─ Dies on DB drop/commit        └─ Can transfer to transaction   └─ Freed by alpm_trans_release
```

#### 1. Database Packages
* Obtained from `alpm_db_get_pkg`, `alpm_db_get_pkgcache`, or dependency satisfiers.
* Owned exclusively by `alpm_db_t`.
* **Must never be passed to `alpm_pkg_free`.** Freeing causes heap corruption.
* Valid only while the owning database and its current cache generation exist.

#### 2. File-Loaded Packages
* Allocated by `alpm_pkg_load(handle, filename, full, level, &pkg)`.
* Owned by the caller.
* Must be explicitly freed by the caller using `alpm_pkg_free(pkg)`.
* Holds references to the handle's configuration but is not attached to any database.

#### 3. Transaction Hand-over Dynamics
* When passing a package to `alpm_add_pkg(handle, pkg)`:
  * **Database package:** `libalpm` only borrows the package. Releasing the transaction does not free it.
  * **File-loaded package:** `libalpm` assumes full ownership of the package.
  * *Contract (`alpm.h`):* "If the package was loaded by `alpm_pkg_load()`, it will be freed upon `alpm_trans_release()` invocation."
  * **Critical Hazard:** Calling `alpm_pkg_free` on a file-loaded package after adding it to a transaction causes a double-free crash during `alpm_trans_release`.

---

### 3.4 Transactions (`alpm_trans_t`)

A transaction executes atomic package installations, upgrades, and removals.

#### Lifecycle Phases
1. **Initialization:** `alpm_trans_init(handle, flags)` allocates the transaction struct and acquires `db.lck`.
2. **Staging:** `alpm_add_pkg` and `alpm_remove_pkg` stage target operations.
3. **Preparation:** `alpm_trans_prepare(handle, &data)` computes dependency graphs. On failure, it populates `data` with an error list.
4. **Commit:** `alpm_trans_commit(handle, &data)` applies filesystem changes, executes hooks, and rebuilds local database state.
5. **Release:** `alpm_trans_release(handle)` frees transaction memory, frees added file-loaded packages, and removes `db.lck`.

#### Payload Ownership on Failure
When `alpm_trans_prepare` or `alpm_trans_commit` fails:
* `libalpm` writes an allocated `alpm_list_t*` into `data`.
* The caller owns this list and its contents.
* The element destructor depends strictly on `alpm_errno`:

| Reported Error | Element Type | Required Element Destructor |
| :--- | :--- | :--- |
| `ALPM_ERR_PKG_INVALID_ARCH` | `char*` | Standard `free` |
| `ALPM_ERR_PKG_INVALID` | `char*` | Standard `free` |
| `ALPM_ERR_PKG_INVALID_CHECKSUM` | `char*` | Standard `free` |
| `ALPM_ERR_PKG_INVALID_SIG` | `char*` | Standard `free` |
| `ALPM_ERR_UNSATISFIED_DEPS` | `alpm_depmissing_t*` | `alpm_depmissing_free` |
| `ALPM_ERR_CONFLICTING_DEPS` | `alpm_conflict_t*` | `alpm_conflict_free` |
| `ALPM_ERR_FILE_CONFLICTS` | `alpm_fileconflict_t*` | `alpm_fileconflict_free` |

After freeing elements, the list nodes must be freed with `alpm_list_free`.
Failing to free `data` causes unmanaged memory leaks.
Calling the wrong destructor aborts the process.

---

### 3.5 Linked Lists (`alpm_list_t*`)

`libalpm` uses doubly linked lists throughout its public API with dual ownership semantics:

#### Borrowed Lists
* Examples: `alpm_get_syncdbs`, `alpm_db_get_pkgcache`, `alpm_pkg_get_depends`, `alpm_pkg_get_files`.
* The library manages list nodes and item payloads.
* The caller must not modify or free borrowed lists.
* Nodes remain valid only as long as the parent object remains unmutated.

#### Caller-Owned Lists
* Examples: `alpm_checkdeps`, `alpm_checkconflicts`, `alpm_db_search`, `alpm_find_group_pkgs`, transaction failure lists.
* `libalpm` allocates list nodes on behalf of the caller.
* The caller must free list nodes using `alpm_list_free`.
* Payload ownership varies:
  * `alpm_db_search` / `alpm_find_group_pkgs`: Payloads are borrowed `alpm_pkg_t*` pointers. Freeing payloads causes use-after-free crashes.
  * `alpm_checkdeps`: Payloads are allocated `alpm_depmissing_t*` structs. Caller must free them using `alpm_depmissing_free`.
  * `alpm_checkconflicts`: Payloads are allocated `alpm_conflict_t*` structs. Caller must free them using `alpm_conflict_free`.

---

### 3.6 Auxiliary Streams and Entities

#### Package File Lists (`alpm_filelist_t`)
* Array of `alpm_file_t` (`name`, `size`, `mode`).
* Owned by `alpm_pkg_t`.
* Borrowed view. Valid only while the owning package remains alive.

#### Changelog Stream
* `alpm_pkg_changelog_open(pkg)` returns an unmanaged archive handle `void* fp`.
* `alpm_pkg_changelog_read(buf, size, pkg, fp)` reads chunks.
* `alpm_pkg_changelog_close(pkg, fp)` closes the handle.
* The stream requires `pkg` to remain allocated throughout open, read, and close cycles.

#### Mtree Reader
* `alpm_pkg_mtree_open(handle, pkg)` returns an unmanaged `struct archive*`.
* Stepped via `alpm_pkg_mtree_next(pkg, archive, &entry)`.
* Closed via `alpm_pkg_mtree_close(pkg, archive)`.
* Requires `pkg` and `handle` to remain alive until closed.

---

### 3.7 Callbacks and Transient Frames

`libalpm` communicates progress, log messages, and interactive questions through function pointers registered on `alpm_handle_t`:

#### Event Callbacks (`alpm_cb_event`)
* Receives `alpm_event_t*`.
* The union is allocated on the C stack or in transient internal scratch memory.
* It is valid **only** during the callback execution.
* Storing the pointer causes a use-after-free bug.

#### Question Callbacks (`alpm_cb_question`)
* Receives `alpm_question_t*`.
* `libalpm` pauses execution and waits for synchronous answers.
* The callback must write responses directly into the union fields before returning.
* `libalpm` reads the answer immediately upon return.
* Any asynchronous or deferred answering pattern reads uninitialized or stale data.

#### Context Pointer (`ctx`)
* Callbacks receive an opaque user pointer (`void* ctx`).
* The native library retains this pointer until a new callback is registered or the handle is released.
* The caller must ensure the memory referenced by `ctx` outlives all native callback invocations.

---

## 4. Native Invalidation Cascade Matrix

This matrix maps native operations to their immediate and cascading invalidation effects:

| Triggering Native C Call | Immediately Freed / Mutated | Cascading Invalidations | What Remains Valid |
| :--- | :--- | :--- | :--- |
| `alpm_release(handle)` | `alpm_handle_t`, all sync DBs, local DB, all options | All packages, caches, server lists, and file lists | Nothing derived from `handle` |
| `alpm_db_unregister(db)` | `alpm_db_t`, server lists, cache lists | All `alpm_pkg_t*` and file lists in this DB | Sibling databases, handle |
| `alpm_unregister_all_syncdbs(h)` | All sync `alpm_db_t` structs and lists | All sync `alpm_pkg_t*` pointers | Local database, handle |
| `alpm_db_update(db)` | Old `pkgcache` and `grpcache` in `db` | All prior `alpm_pkg_t*` pointers from this DB | `alpm_db_t` struct, server lists |
| `alpm_db_set_servers(db)` | Old internal server list | Stored server list node pointers | `alpm_db_t`, all `alpm_pkg_t*` |
| `alpm_db_set_cache_servers(db)`| Old internal cache-server list | Stored cache-server list pointers | `alpm_db_t`, all `alpm_pkg_t*` |
| `alpm_add_pkg(h, loaded_pkg)` | Caller ownership of `loaded_pkg` | None (ownership transferred to transaction) | Package pointer, transaction |
| `alpm_trans_commit(h)` | Local database `pkgcache` | All prior local `alpm_pkg_t*` pointers | Sync databases, handle |
| `alpm_trans_release(h)` | `alpm_trans_t`, all added file packages | Added package pointers, transaction lists | Databases, session handle |
| Callback Return | Stack-allocated event/question union | Any pointer referencing the callback frame | Outer session state |

