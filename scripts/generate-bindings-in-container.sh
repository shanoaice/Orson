#!/usr/bin/env bash
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
image="${ORSON_ALPM_TEST_IMAGE:-${PACPAR_ALPM_TEST_IMAGE:-orson-alpm-tests}}"

# Build container image containing canonical Arch Linux toolchain if needed
docker build -f "$repo_root/containers/alpm-tests/Containerfile" -t "$image" "$repo_root"

# Run binding generation inside canonical Arch Linux container
docker run --rm -v "$repo_root:/workspace:Z" -w /workspace "$image" cargo build -p libalpm-sys-cs "$@"
