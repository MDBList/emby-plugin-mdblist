#!/usr/bin/env bash
# Build the plugin and deploy it into the local dev Emby server's plugin
# folder, then restart the container and tail its logs.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEV_DIR="$ROOT/dev"
PLUGIN_DIR="$DEV_DIR/config/plugins"
PUBLISH_DIR="$ROOT/Emby.Plugin.MDBList/bin/Debug/net8.0"

dotnet build "$ROOT/Emby.Plugin.MDBList/Emby.Plugin.MDBList.csproj" -c Debug

mkdir -p "$PLUGIN_DIR"
cp -f "$PUBLISH_DIR/Emby.Plugin.MDBList.dll" "$PLUGIN_DIR/"

cd "$DEV_DIR"
docker compose restart emby

echo "Deployed. Tailing logs (Ctrl+C to stop)..."
docker compose logs -f emby
