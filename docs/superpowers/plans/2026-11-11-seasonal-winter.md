# Seasonal Harsh Winter Implementation Plan

Goal: Enable generated snow textures only during the authoritative server winter.
Architecture: Authenticated game bridge publishes calendar state to a small HTTPS endpoint. The signed launcher checks fresh state and builds textures from each player's own game files into an isolated cache, then moves only its managed winter directory in/out of Mods while the game is stopped. Server notifies connected players when a winter boundary requires a full game restart.
Tech stack: existing C# .NET 10 launcher, Harsh Winter 4.5.1 C# builder, Lua server, Endora PHP.
Spec: User-approved seasonal winter approach in this conversation, November 11 2026.

Constraints: Preserve existing gameplay/mods. Test override must not change texture season. Never redistribute derived base-game assets. No silent process termination. Never mutate active game files while running. No automatic destructive server restart.
Review focus: stale/offline calendar; season change while building; interrupted build; unmanaged winter directory; existing mod_order file.

- [x] Add validated public season endpoint behind existing authenticated bridge; tests reject stale and inconsistent season/date.
- [x] Add actual calendar publication independent of /zimatest, and winter-boundary notices; Lua tests.
- [x] Add isolated texture-only builder (disable extras, clothing, loading). Validate generated archive contents locally.
- [x] Add launcher season validation and transactional directory switch; tests cover both boundaries, crash states and foreign files.
- [x] Build and publish signed launcher, deploy backed-up server/web files, verify hashes and endpoint.
- [x] Document restart requirement and explicitly distinguish local tests from actual rendered-world tests.

Verification: source 39fad77; signed sequence 18; launcher 1.1.5. Live server publication verified. Actual rendered winter scene remains a game-test item, not a claimed result.
