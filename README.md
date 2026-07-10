# The Long Dark - Seamless Interiors (Currently, it's just the Camp Office)
A massive overhaul mod developed for The Long Dark that aims to integrate all interior buildings directly into the open world without any loading screens. Walk through doors seamlessly and experience the interior and exterior as one unified environment!

(Note: This project is in active development. Currently supported locations are listed in the updates, starting with the Camp Office!)

✨ Features
Additive Scene Integration: Directly loads and merges interior scenes into their respective exterior environments. This bypasses traditional loading screens, creating a strictly unified instance across the map.

Dynamic Weather Culling: Uses mathematically calculated interior bounds to deploy custom ParticleKillerInstance zones. This instantly culls outdoor snow and wind particles inside structures without relying on hard scene transitions.

Real-Time Audio Occlusion: Hooks into the game's GameAudioManager to dynamically apply HeavyOcclusion upon entering a building, ensuring realistic acoustic dampening of exterior weather and wildlife.

Strict AI Pathfinding & Collision Boundaries: Generates a precise, hollow BoxCollider perimeter combined with custom NavMeshObstacle (carving enabled) components around interiors. This physically prevents wildlife from clipping through walls during high-velocity flee or scent-tracking behaviors.

Deterministic Loot Synchronization: Automatically handles spatial deduplication and PDID generation for gear items upon the first load, ensuring containers and originally spawned items transition flawlessly into the merged exterior space.

⚙️ Installation
Ensure you have MelonLoader installed on your PC.

Download the latest version of the mod (e.g., SeamlessInteriors.dll) from the Releases section of this repository.

Copy the downloaded .dll file into the Mods folder located in your The Long Dark game directory.

Launch the game and enjoy your seamless world!

⚠️ Important Notes & Known Issues
Initial Loading: When you first load into the game (or your existing save file), the mod synchronizes items and containers with the outside world. This background process only happens once per location during the initial load.

Compatibility: This mod may conflict with other mods that alter the physical structure or interior layout of the buildings.

🤝 Credits
Thanks to Hinterland Studio for this amazing game.

Thanks to MelonLoader and The Long Dark modding community.


