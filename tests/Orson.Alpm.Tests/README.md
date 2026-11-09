# Orson.Alpm.Tests

This project contains the C# test harness for `Orson.Alpm`.

## Test layout

- `Unit/Callbacks/` — the callback thunks and registration, and the log `va_list` forwarding.
- `Unit/Databases/` — `Database` behavior: search, update, servers, usage.
- `Unit/Errors/` — errno classification, the `Try*` seam, and what a failed transaction reports.
- `Unit/Extensions/` — optional downstream extensions: capability detection, supported and unsupported paths, and argument/lifetime guards.
- `Unit/GcAnchor/` — the IL audit that every read of unmanaged memory names its anchor, plus the IL decoder.
- `Unit/Interop/` — unmanaged interop pairing and buffer marshaling helpers.
- `Unit/ObjectLifetime/` — the domains and stamps themselves, the sites that invalidate them, and the path a dropped session takes through the GC.
- `Unit/Options/` — query and modification of libalpm options.
- `Unit/Ownership/` — which side owns a package, a list or a transaction.
- `Unit/Packages/` — reading a package: files, mtree, changelog, dependency resolution.
- `Unit/Session/` — `AlpmSession`-level APIs that need no database: unlock, logaction, fetch, dependency checks, version and capabilities.
- `Unit/Snapshots/` — the managed values copied out of native memory, and the enums that mirror their native counterparts.
- `Fixtures/` — reusable environment setup that can also be shared by future F# tests.
- `native/` — test-only C helper for the `va_list` forwarding test, compiled by this project into its intermediate output.

## Running locally

The host must provide `libalpm`, a C compiler (`cc`, from `gcc` or `clang`), and the .NET SDK. Rust and `pkg-config` are only required when regenerating bindings (`-p:GenerateBindings=true`).

```bash
dotnet test
```

Or targeting the test project specifically:

```bash
dotnet test --project tests/Orson.Alpm.Tests/Orson.Alpm.Tests.csproj
```

Or executing the xUnit v3 test binary directly:

```bash
./tests/Orson.Alpm.Tests/bin/Debug/net10.0/Orson.Alpm.Tests
```

No filter is needed: every test here runs on the host. The ones that touch libalpm do it through
`IsolatedAlpmEnvironment`, so nothing in the suite needs the project container or a system database.
(An older instruction filtered out tests whose names contained `Integration`; the tests it referred to
are gone.)

## Running in an OCI container

Use `scripts/run-alpm-tests-in-container.sh`. The script auto-detects `podman`, `nerdctl`, or `docker`, and builds `containers/alpm-tests/Containerfile`.
