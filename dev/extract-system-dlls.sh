#!/usr/bin/env bash
# Extracts the real MediaBrowser.*.dll reference assemblies from the pinned
# emby/embyserver image (see docker-compose.yml) into dev/system/, since
# Emby doesn't publish current NuGet packages for plugin development --
# confirmed against a real, currently-maintained community plugin, which
# references these same DLLs the same way. Re-run this after bumping the
# pinned image tag in docker-compose.yml.
set -euo pipefail

DEV_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$DEV_DIR"

docker compose up -d
docker compose cp emby:/system/. ./system/
echo "Extracted $(ls system/*.dll | wc -l | tr -d ' ') DLLs into $DEV_DIR/system/"
