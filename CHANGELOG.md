<!-- markdownlint-disable MD024 -->
# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Common Changelog](https://common-changelog.org/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [0.2.1] - 2026-11-09

### Added

- Add dynamic P/Invoke extensions for CachyOS `libalpm` fork features ([`6837a44`](https://github.com/shanoaice/Orson/commit/6837a44198774753bda577ad531f6d21dd6835e8))

## [0.2.0-preview.1] - 2026-11-06

### Removed

- **Breaking:** remove unmerged upstream network sandbox options (`AlpmOptions.Sandbox`) to align with canonical Arch Linux `libalpm` ([`be37ab3`](https://github.com/shanoaice/Orson/commit/be37ab35198d67f27bad35b1bbb51f37f215d9b6))

## [0.1.0-preview.1] - 2026-11-06

### Added

- Add `AlpmSession` for managing `libalpm` initialization, filesystem root, and pacman database path
- Add `Database` for querying local and sync repository databases, packages, and groups
- Add zero-allocation borrowed `PackageView` to query package metadata directly from native memory
- Add hierarchical lifetime token tree (`LifetimeStamp`, `LifetimeFacet`) to guard unmanaged memory from use-after-free errors
- Add `PackageSnapshot` to copy package metadata into managed memory for beyond-lifetime or cross-thread retention
- Add `AlpmTransaction`  supporting package installation, removal, target preparation, and system upgrades
- Add managed callback dispatch for download progress, log output, and question resolution
- Add question write-back handling for interactive resolution
- Add package MTREE manifest reader and `libarchive` archive entry inspection
- Add `PackageVersion` value equality comparisons`
- Add typed domain exception hierarchy mapped to native `alpm_errno` values
- Add `SafeAlpmHandle` for leak-free resource cleanup using .NET runtime safe handles
- Add Source Link support and NuGet symbol package distribution

[0.2.1]: https://github.com/shanoaice/Orson/compare/v0.2.0-preview.1...v0.2.1
[0.2.0-preview.1]: https://github.com/shanoaice/Orson/compare/v0.1.0-preview.1...v0.2.0-preview.1
[0.1.0-preview.1]: https://github.com/shanoaice/Orson/releases/tag/v0.1.0-preview.1
