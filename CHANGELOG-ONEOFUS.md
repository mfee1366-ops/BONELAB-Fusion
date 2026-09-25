# OneOfUs Fusion Changes

## Fusion 1.14.2 compatibility build

See `OPTIMIZATIONS.md` for detailed networking notes.

- Added compatibility work for BONELAB/Fusion 1.14.1 and 1.14.2 while reporting 1.14.2.
- Added dedicated-server lifecycle, persistent configuration, live map/settings changes, moderation, and player controls.
- Added host-limit bypass and map prop lifecycle commands.
- Added optimized relevance filtering, pose traffic, network serialization, and server metrics.
- Improved mod.io authentication and multiplayer missing-mod downloads.
- Added pooled receive payloads and thread-local reader/writer reuse.
- Removed the extra packet-envelope buffer rental and copy.
- Replaced the host population O(N^2) scan with an O(N) centroid and bounded stale prop relevance state.
- Added a VRChat-style blue diamond fallback for missing, downloading, failed, and distant avatars.
- Added missing-spawned-item confirmation with requester name and download/install action.
- Added host proximity prop ownership so props near players keep simulating when their owner leaves or is a dedicated server.
- Far and zone-culled players now stay visible as a diamond; the host keeps every pose current.
- Added optional "Rotate When Grabbed" setting.
- Added avatar motion smoothing for remote players.
- Network sync is fixed to 20–30 Hz (default 30); nearby players never drop below 20 Hz.
- Physics runs at 200 Hz by default; far players sleep their physics; host relay culling is off by default in favour of client culling.
- Fixed grips staying attached after leaving a server, pitch and roll after unragdolling, and choppy remote bodies.
- Fixed a laser cursor error with flatscreen controllers and a failed download for empty spawn barcodes.
- Mod downloads run in parallel, merge duplicate requests, and no longer stall after a failed copy.

## OneOfUs Launcher

- Added Servers, Code Mods, mod.io Browser, Fusion Profile, Dedicated Server, and Credits tabs.
- Added persistent mod.io device authentication, search filters, sorting, subscriptions, installed checks, and installation.
- Added map imagery, local map scanning, server player avatars, roles, permissions, and moderation actions.
- Dedicated-server settings are saved immediately and applied live where supported.
