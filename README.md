# emby-plugin-mdblist

An Emby server plugin that syncs watched status and ratings two-way with
[MDBList](https://mdblist.com), pushes library/collection membership, and
reports live playback progress via scrobbling. Uses MDBList's incremental
sync API (`/sync/last_activities` + `/sync/journal`) for cursor-based
updates rather than full-library reconciliation on every run.

This is a port of the design proven in
[jellyfin-plugin-mdblist](https://github.com/linaspurinis/jellyfin-plugin-mdblist)
(itself a port of [kodi-mdblist-scrobbler](https://github.com/linaspurinis/kodi-mdblist-scrobbler)).

## Status

Early development.

## Local development

Requires the .NET 9 SDK (`brew install dotnet@9` on macOS) and Docker.

```sh
cd dev
docker compose up -d
```

This starts a local Emby Server 4.10.0.30 on `http://localhost:8097`, and
extracts the real `MediaBrowser.*.dll` reference assemblies from that same
pinned image into `dev/system/` (no current NuGet packages exist for these
-- see the plan for why).
