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

## Installing

Emby has no self-hosted plugin-repository mechanism for third-party plugins
(unlike Jellyfin/Kodi) -- manual installation is the only real distribution
path, confirmed against Emby's own guidance:

1. Download the latest `emby-plugin-mdblist-*.zip` from the
   [Releases page](https://github.com/linaspurinis/emby-plugin-mdblist/releases).
2. Extract it -- you'll get a single `Emby.Plugin.MDBList.dll`.
3. Copy that file into your Emby Server's `plugins` folder (e.g.
   `/config/plugins` in the official Docker image, or
   `%ProgramData%\Emby-Server\plugins` on Windows).
4. Restart Emby Server.
5. Go to **Dashboard → Plugins → MDBList** to open its config page:
   - Pick the Emby user to link.
   - Click **Connect to MDBList** and follow the device code flow (visit the
     shown URL, enter the code, approve on MDBList).
   - Choose which categories to sync (watched status, ratings, collection,
     live scrobbling) and save.

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
