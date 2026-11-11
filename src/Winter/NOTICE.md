# Harsh Winter texture builder

Source: Harsh Winter 4.5.1 Czech version, Nexus mod 3808, user-supplied archive (2026-10-13).
https://www.nexusmods.com/kingdomcomedeliverance2/mods/3808

The original Winter.*.cs sources, winter_rules.txt and snow_detail.bin are included with the author permission reported by the server owner. Original source comments are preserved.

Kroniky modification: Winter.Core.cs does not run WinterExtras.Run. The entry point only calls textures/native with noLoading=true, using an isolated output directory. Clothing, survival, loading-screen and footprint changes are not enabled. Generated game textures remain on each player's own PC and are never uploaded to GitHub.
