# Orson.Alpm

> Preview version. APIs may change.

C# binding for Arch Linux's package management library, `libalpm`.

## Prerequisites

This library does not bundle any native libraries. Libraries are loaded from system via P/Invoke.

- `pacman`: latest version preferred. Older versions without API/ABI differences might work, but there is no guarantee.
If you are on a working Arch Linux installation, it is already provided. Other pacman-based distribution may or may not work depending on their shipped version.
- `libarchive`: optional. Only required if you need to work with MTree data.
On Arch Linux, `libarchive` is a dependency of `pacman`, so it is also provided. Consult package catalog for other pacman-based distribution.

## Quickstart

```csharp
using Orson.Alpm;

// 1. Initialize libalpm with system root and pacman database path
using var alpm = new AlpmSession(root: "/", dbpath: "/var/lib/pacman");
var localDb = alpm.GetLocalDatabase();

// 2. Query zero-allocation borrowed views
var pkg = localDb.GetPackage("glibc");
if (pkg != null)
{
    Console.WriteLine($"{pkg.Name} {pkg.Version} ({pkg.InstalledSize} bytes)");
}

// 3. Retain data beyond session lifetime
PackageSnapshot snapshot = pkg!.ToSnapshot(includeFiles: false);
```

### Core Invariants

- **Borrowed memory**: `PackageView` directly borrows unmanaged memory. It is valid only while the parent `AlpmSession` and `Database` remain open. Accessing an invalidated view throws `AlpmLifetimeException`.
- **Snapshots**: Call `pkg.ToSnapshot()` to copy metadata into managed memory for cross-thread or long-term retention.
- **Single-threaded**: An `AlpmSession`, its databases, and its views belong to a single thread at a time.

For more information and usage, see the [documentation](https://shanoaice.github.io/Orson/).
