#!/usr/bin/env bash
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
runtime="${ORSON_CONTAINER_RUNTIME:-${PACPAR_CONTAINER_RUNTIME:-}}"
image="${ORSON_ALPM_TEST_IMAGE:-${PACPAR_ALPM_TEST_IMAGE:-orson-alpm-tests}}"

if [[ -z "$runtime" ]]; then
  for candidate in podman nerdctl docker; do
    if command -v "$candidate" >/dev/null 2>&1; then
      runtime="$candidate"
      break
    fi
  done
fi

if [[ -z "$runtime" ]]; then
  printf 'No supported container runtime found. Set ORSON_CONTAINER_RUNTIME to podman, nerdctl, or docker.\n' >&2
  exit 1
fi

# Build container image containing canonical Arch Linux toolchain if needed
"$runtime" build -f "$repo_root/containers/alpm-tests/Containerfile" -t "$image" "$repo_root"

# Cargo fingerprints build.rs itself, not the source files it writes. Because
# target/ is shared with host builds, the script can be marked fresh while the
# generated bindings hold symbols from the host's libalpm. Clean the package so
# build.rs always regenerates them from the container's canonical header.
"$runtime" run --rm -v "$repo_root:/workspace:Z" -w /workspace "$image" \
  sh -c 'cargo clean -p libalpm-sys-cs && exec cargo build -p libalpm-sys-cs "$@"' sh "$@"
