# Fusion Optimized

This fork starts from `Lakatrazz/BONELAB-Fusion` commit
`4b0505be680b3232f3b2db862a3dcd21bba43de7` and keeps its MIT license.

## Implemented

- Native Windows launcher using Fusion's SteamVR matchmaking lobby metadata.
- Direct BONELAB launch and automatic `SteamVR` login/join via `--fusion-code`.
- Host relay culling (setting "Host Relay Culling", off by default): when on, the host filters player poses (100 m), prop poses (75 m),
  and voice (35 m) per recipient. By default the host forwards everything and each client culls for itself ("Client Culling": distant prop poses and voice are skipped locally). Reliable gameplay messages are never filtered.
- Network sync runs at 20–30 Hz (default 30) while the game renders and simulates at the headset rate. Players within relevance range always sync at the full tick rate; busy lobbies (8+/24+ players) trim to 25/20 Hz but never below 20 Hz. Players beyond range get 2 Hz (shown as the diamond). The dedicated server runs at twice the tick rate so relayed updates stay evenly spaced.
- Adaptive prop pose rate: full tick rate fast, half slow, quarter nearly settled, and
  0 Hz after sleep. Existing reliable final sleep pose is retained.
- Incoming parsing no longer calls `message.Buffer.ToArray()` for the complete
  transport packet. Only the payload whose lifetime can outlive the callback is copied.
- `NetMessage` storage uses `ArrayPool<byte>` and pinned reusable buffers instead
  of `AllocHGlobal`/`FreeHGlobal` for every message.
- Module envelopes serialize directly into their pooled writer without an
  intermediate expanded array.
- Matchmaking ordering uses `ThenBy` correctly instead of repeatedly discarding
  prior ordering with `OrderBy`.
- Runtime counters in `NetworkMetrics`: bytes and packets by message tag, relay
  recipients, active moving props, allocations in the network update, last/p95/
  worst update duration.
- Existing catch-up transfer remains chunked across frames and transient poses
  remain on Steam's unreliable send type.
- Reader and writer objects are thread-local pooled, and message envelope writers transfer their rented buffer directly to `NetMessage`, removing a second rent and full-envelope copy.
- Received payload lifetime copies use pooled arrays and are released after immediate or delayed handlers finish.
- Population hotspot calculation is O(N) using a centroid instead of an O(N^2) densest-neighbor scan. Stale prop-recipient rate entries expire periodically.
- Remote avatars that are missing, downloading, or failed to load are shown as a VRChat-style spinning blue diamond (with nametag) instead of Polyblank. The same diamond replaces distant avatars at 20 m, returning to full detail at 18 m. It is client-side, sized from the synced avatar height, and independent of relay relevance filtering. Renderers are only toggled when the representation changes, not every frame.
- Missing spawned items can require confirmation. The notification identifies the spawning player, shows the barcode, and downloads only after acceptance.
- Proximity prop ownership (host): when a prop's owner has culled it, walked more than 40 m away, or is a dedicated server, the host hands it to the nearest player within 25 m. That player simulates it and the poses reach the host and everyone else. It uses the existing ownership response message, so unmodified clients accept it. Transfers have a 3 s cooldown, and a player who reports the prop as culled is skipped for 30 s.
- The host processes and receives every player and prop pose regardless of distance. Its copy of positions drives relevance filtering for everyone, and it previously went stale beyond 100 m.
- Players beyond the relevance range get 2 Hz pose updates instead of none. Zone-culled players stay visible as the diamond at their synced position.
- Optional grab rotation (setting "Rotate When Grabbed", off by default): a remote hand holding the local rig turns the local player, and so the camera, with the grabber's controller yaw.
- Avatar motion smoothing (setting "Avatar Motion Smoothing", on by default): remote heads, hands, and playspace rotation use an 8-pose snapshot buffer rendered 1.5 update intervals behind (50–350 ms), so uneven or low-rate updates blend smoothly instead of stepping.
- Remote bodies (pelvis) blend out the jump between each new pose over about 100 ms and extrapolate at most 0.25 s, instead of snapping on every update.
- Physics rate (setting "Physics Rate (Hz)", default 200): overrides BONELAB's recommended physics frequency, so the local simulation, remote player bodies and props run at 200 Hz. Rendering stays at the headset rate and network sync stays at 20–30 Hz.
- Far player physics sleep: past 30 m (waking at 27 m) a remote player's physics rig is hibernated on this client and shown as the diamond at their synced position.
- The fallback diamond also shows while an avatar is loading; unsupported shaders are skipped and the chosen shader is logged.
- Grips break when leaving a server, and hands holding a player who leaves are released.
- After unragdolling, the local playspace is leveled for 1 s and remote players are reset upright; received playspace rotation is yaw only.
- Relevance checks cache player positions per frame and prop positions per message; the ownership limit uses a per-owner count refreshed 4 times a second instead of scanning every entity per request.
- Removed the 1 Hz per-prop heartbeat that clients sent for every owned prop; the periodic host resync only resends props whose pose changed.
- Mod downloads: up to 3 parallel downloads; level downloads jump the queue instead of cancelling queued avatar downloads; identical barcode requests are merged; a failed copy no longer blocks all later downloads; stalled downloads free their slot after 5 minutes.

## Requested 20-point audit

1. Host fan-out: relevance filtering and per-sender/per-recipient rate limiting run before individual sends. Steam exposes no multicast primitive.
2. Player poses: always sent at the 20–30 Hz tick rate; the host trims busy lobbies to no lower than 20 Hz; the legacy fixed wire format remains.
3. Props: movement checks, sleep suppression, adaptive 20/10/5 Hz rates, relevance, and a final reliable sleep pose are active.
4. Voice: capture VAD and spatial host routing are active; inaudible recipients are skipped.
5. Receive copying: packets are header-parsed from their native span; retained payloads use pooled arrays instead of new arrays.
6. `NetMessage`: pinned pooled buffers replace per-message unmanaged allocation.
7. Serializer allocation: poses are reused where the API permits, and reader/writer envelopes are pooled.
8. Batching: capability-gated because legacy clients cannot decode a batch envelope.
9. Interest management: player, prop, and voice ranges plus population-aware rates are active.
10. Adaptive tick rates: active for players and props; sleeping props send zero transient updates.
11. Deltas: unchanged whole poses are skipped; component masks require a negotiated packet format.
12. Bursts: non-critical catch-up work is frame-budgeted while reliable gameplay ordering is retained.
13. Catch-up: transfer is chunked and live transient state supersedes stale movement state.
14. Reliability: poses and voice are unreliable; final sleep, ownership, spawn, and gameplay events stay reliable.
15. Client receive: pooled readers/payloads and indexed handlers remove common allocation and dispatch costs.
16. Player rendering: missing/failed avatars and distant avatars are replaced by a single blue diamond renderer with hysteresis; irrelevant avatars are hidden.
17. Dispatch: byte tags use a direct handler array and compact byte/ushort IDs remain.
18. Relay serialization: one serialized message is reused for every recipient; envelope double-copying is removed.
19. Pooling: packet arrays, readers, writers, and message storage are pooled.
20. Metrics: bytes/packets by tag, recipients, active props, allocations, handler time, and current/p95/worst tick are recorded.

## Compatibility and deliberate limits

This build is versioned 1.15.0, while its advertised network protocol remains
on the upstream 1.14 line. Wire formats are unchanged, so it can communicate
with upstream Fusion 1.14.1 and 1.14.2. Packet batching and change-mask/delta wire formats are not enabled in
this compatibility build: both require a negotiated protocol capability or a
version break, otherwise an older client interprets the packet incorrectly.
Steam networking sockets also do not provide true multicast groups; spatial
recipient filtering is applied before individual relay sends.

## Build and run

Set `BONELAB_DIR` to the game folder and build `LabFusion/LabFusion.csproj`.
Install the resulting `LabFusion.dll` into BONELAB's `Mods` folder. Run
`FusionLauncher.exe`, click **Connect with Steam**, choose a lobby, then click
**Join selected server** (or double-click the row).

Steam must be running and signed in. The launcher targets the same SteamVR app
ID (`250820`) used by Fusion's SteamVR network layer and launches BONELAB app
ID `1592190` through Steam.
