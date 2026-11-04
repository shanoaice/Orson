# GUI Architecture and UX Design Considerations

This document records the architectural and user-experience decisions for an Orson-based graphical package manager.

---

## 1. Target Audience and Design Philosophy

- Target users who are unfamiliar with Linux terminals.
- Target Windows switchers and non-technical desktop users.
- Reject raw, tabular package managers like YaST 2 or Synaptic.
- Avoid exposing 15,000 raw library packages directly to casual users.
- Strive for a modern application store experience similar to Shelly, GNOME Software, or Discover.
- Maintain full-system package management integrity under the hood.

---

## 2. Metadata Bridge: ALPM and AppStream

### 2.1 The Native Metadata Defect
- Native `libalpm` sync databases contain minimal metadata (`desc` files).
- They provide only package name, version, one-line summary, URL, license, and size.
- They lack application icons, screenshots, categories, and rich descriptions.
- Over 90% of repository packages are libraries, headers, or runtime modules.

### 2.2 Two-Tier UX Model
To resolve this limitation, the GUI combines `libalpm` with `archlinux-appstream-data`:

```
┌────────────────────────────────────────────────────────────────────────┐
│                        Two-Tier Application Model                      │
├──────────────────────────┬─────────────────────────────────────────────┤
│ Tier 1: "Applications"   │ - Filtered by AppStream / .desktop files    │
│ (Default / Store View)   │ - Rich cards, icons, screenshots, and tags  │
│                          │ - Categorized: Internet, Office, Media, etc.│
├──────────────────────────┼─────────────────────────────────────────────┤
│ Tier 2: "System Packages"│ - Full 15,000+ package database             │
│ (Advanced / Tab View)    │ - Grouped: CLI tools, libraries, base       │
│                          │ - Target: Power users and system maintenance│
└──────────────────────────┴─────────────────────────────────────────────┘
```

1. **Tier 1: Applications (Store View)**:
   - The default view displays only packages with a desktop file (`/usr/share/applications/*.desktop`).
   - AppStream maps package names to application icons, screenshots, and categories.
   - Users interact with friendly names like "VLC Media Player" instead of `vlc`.
   - The engine resolves and installs underlying ALPM dependencies automatically.

2. **Tier 2: System Packages (Advanced View)**:
   - An advanced tab or toggle exposes the complete ALPM database.
   - The UI categorizes non-desktop packages using filesystem layout:
     - **CLI Utilities**: packages placing binaries in `/usr/bin/` without desktop files.
     - **Libraries and Runtimes**: packages in `/usr/lib/` or matching `lib*` patterns.
     - **Base System**: kernel, firmware, and systemd modules.
   - This keeps the system transparent without cluttering the primary store.

---

## 3. Provider Selection (`SelectProvider`) Architecture

### 3.1 Rejection of Curated Recommendation Databases
- Do not maintain a manual database of provider recommendations.
- Packaging practices in Arch Linux shift continuously.
- Curated recommendations create maintenance debt and become obsolete quickly.

### 3.2 Native Heuristics and Objective Badging
The GUI evaluates objective native signals to guide user selection:

1. **Pre-Select the ALPM Default**:
   - `libalpm` orders providers by repository priority.
   - Pre-select index `0` (`use_index = 0`) with a `(Default)` badge.
   - This matches canonical `pacman` behavior.

2. **Installed System Stack Proximity**:
   - Inspect the current `LocalDatabase`.
   - If `pipewire` is already installed, mark `pipewire-jack` with `Matches System Audio`.
   - If Intel GPU drivers are installed, mark `vulkan-intel` accordingly.

3. **Metadata Clarity**:
   - Present candidate packages as clean selectable cards.
   - Display package name, repository, installed size, and native description.

4. **Automatic Mode**:
   - Provide an optional setting: *"Automatically choose default providers"*.
   - When enabled, the GUI answers index `0` automatically without prompting.

---

## 4. Upgrade Integrity: Strict Prohibition of Partial Upgrades

### 4.1 The Rolling Release Invariant
- Arch Linux strictly prohibits partial upgrades.
- Updating individual repository packages breaks shared library dynamic links (`.so` bumps).
- Staged or piecemeal package updates lead to system crashes.

### 4.2 All-or-Nothing Repository Transactions
- Official repository upgrades (`core`, `extra`, `multilib`) must execute as a single atomic transaction.
- Do not present checkboxes to deselect individual official repository packages.
- Place any partial upgrade switches in a "Dangerous Settings" section with explicit warnings.

### 4.3 Safe Domain Separation
The GUI separates updates only across distinct packaging domains:

1. **Official Repositories**:
   - Upgraded strictly as an atomic whole (`pacman -Syu`).
   - Triggered by a unified **"Update System"** button.

2. **AUR Packages**:
   - Handled in an independent build queue after official upgrades finish.

3. **Flatpak / Sandboxed Runtimes**:
   - Updated independently without risk to host system libraries.

4. **Firmware (`fwupd` / LVFS)**:
   - Managed via separate hardware update mechanisms.

### 4.4 Informational Grouping (Read-Only)
The upgrade review presents packages in functional groups purely for transparency:
- **Desktop Applications**: packages with user-facing desktop interfaces.
- **System Libraries & Drivers**: low-level components and shared libraries.
- **Reboot Warning Banner**: displayed whenever the kernel, graphics drivers, or `systemd` update.

---

## 5. Interaction and Search Paradigm

- Unlike TUI mode, GUI controls utilize asynchronous virtualized views with double-buffering.
- Local repository searches run against in-memory `PackageView` caches with debouncing (150–250 ms).
- Remote AUR queries execute only after debouncing or explicit user submission to prevent RPC rate-limiting.
- Background worker threads scan packages using [PackageView.cs](../src/Orson.Alpm/Package/PackageView.cs).
- Worker threads transfer immutable [PackageSnapshot.cs](../src/Orson.Alpm/Package/PackageSnapshot.cs) models to the UI dispatcher.

---

## 6. See Also

- [CLI Interface Considerations](./cli-interface-considerations.md)
- [ADR 0020: Allocation-Light Boundaries](../docs/adr/0020-allocation-light-boundaries-and-callback-hot-paths.md)
- [ADR 0011: Sessions are Single-Threaded](../docs/adr/0011-sessions-are-single-threaded.md)
