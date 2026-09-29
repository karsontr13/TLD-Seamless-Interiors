using MelonLoader;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─────────────────────────────────────────────────────────────────
        // LEAKED CLONE GEAR - duplicates created by the GAME'S OWN save
        //
        // PROBLEM: a clone lives inside the region scene, so its GearItems register
        // themselves with the game's GearManager like any other item in the world.
        // While the player is inside the building MasterInterior is ACTIVE, which means
        // those items are active too - and GearManager.Serialize() writes them into the
        // REGION's scene save.
        //
        // After the next load the same item therefore exists TWICE:
        //
        //   * the clone's copy - spawned from _inactive_scene_gear.json as a child of
        //     MasterInterior, so it is hidden together with the building
        //   * the game's copy  - put back by GearManager.Deserialize as LOOSE REGION
        //     GEAR. It stands at the same world position, but it is parented nowhere
        //     near the clone
        //
        // MasterInterior.SetActive(false) cannot touch the second one. So the player
        // walks out of the building, the building disappears - and its loot stays
        // hanging in the air exactly where the shelves used to be.
        //
        // MEASURED (dam trailers, sandbox29): the five items the stray sweep reported
        // adopting into CampTrailerE_1 after a load - GEAR_Cloth x2, GEAR_Newsprint,
        // GEAR_NewsprintRoll, GEAR_BookE - are exactly five of the eight records in that
        // building's own gear JSON. The other three were never adopted, and those are
        // the ones that stayed visible.
        //
        // WHY ADOPTION IS NOT ENOUGH: AdoptStrayInteriorObjects deliberately demands a
        // volume shrunk by 0.7m plus a roof AND a floor belonging to the clone, so that
        // it can never claim the things the player set down by the door. A trailer's
        // interior is barely two metres across and two metres high, so that shrunk box
        // throws away most of its floor - which is why the trailers show this and the
        // camp office does not.
        //
        // FIX: decide by IDENTITY instead of geometry. A loose item carrying the guid of
        // an item the clone is holding - or standing within a few centimetres of one with
        // the same name - IS that item, copied. The clone's copy is the one the mod owns
        // and saves, so the loose copy is destroyed.
        // ─────────────────────────────────────────────────────────────────

        // How close a loose item has to be to the clone's own copy to count as the same
        // item. The two are written from the same world position, so they land on top of
        // each other; the margin only covers a clone rebuilt a hair off its old spot.
        private const float LEAKED_GEAR_MATCH_RADIUS = 0.12f;

        public static int PurgeLeakedCloneGearDuplicates(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return 0;
            if (!instance.RunCompleted) return 0;

            // Nothing has been restored into this building yet, so whatever is standing
            // in it belongs to the world and not to the clone.
            if (!instance.ContentHydrated) return 0;

            var owned = InteriorScan.Gear(instance.MasterInterior);
            if (owned.Length == 0) return 0;

            Transform masterT = instance.MasterInterior.transform;

            var ownedGuids = new HashSet<string>();
            var ownedCells = new Dictionary<long, List<DedupEntry>>();

            foreach (var gear in owned)
            {
                if (gear == null || gear.gameObject == null) continue;

                // ONLY ITEMS ACTUALLY STANDING IN THE BUILDING.
                //
                // GetComponentsInChildren(true) also returns the ones the player has
                // already picked up: TLD switches those off and leaves them in the
                // hierarchy. Their guids must NOT go into this set - the player is
                // carrying that item, and dropping it a couple of metres outside the
                // door would put it right back inside this building's AABB, where a
                // guid match would destroy it.
                //
                // The same reasoning as ShouldPersistGear, and for the same reason:
                // only a loose item that is standing here can be a copy of one of ours.
                if (!gear.gameObject.activeSelf) continue;
                if (!IsHierarchyActiveUpTo(gear.transform, masterT)) continue;
                if (IsInsideContainer(gear.transform, masterT)) continue;

                var ownedGuid = gear.GetComponent<Il2Cpp.ObjectGuid>();
                if (ownedGuid != null && !string.IsNullOrEmpty(ownedGuid.m_Guid))
                    ownedGuids.Add(ownedGuid.m_Guid);

                DedupEntry entry;
                entry.name = CleanGearName(gear.gameObject.name);
                entry.pos = gear.transform.position;

                long cell = LeakCellKey(entry.pos);
                List<DedupEntry> bucket;
                if (!ownedCells.TryGetValue(cell, out bucket))
                {
                    bucket = new List<DedupEntry>(2);
                    ownedCells[cell] = bucket;
                }
                bucket.Add(entry);
            }

            // Coarse AABB pre-filter, as everywhere else: one float compare rejects
            // almost every item in the region. A leaked copy always stands at the
            // building's own coordinates, so nothing real is filtered away.
            Bounds filter;
            bool hasFilter = TryGetWorldFilterBounds(instance, out filter, 1.6f);

            float sqrRadius = LEAKED_GEAR_MATCH_RADIUS * LEAKED_GEAR_MATCH_RADIUS;
            int purged = 0;

            foreach (var gear in SceneScan.GearAll())
            {
                if (gear == null || gear.gameObject == null) continue;

                Vector3 pos = gear.transform.position;
                if (hasFilter && !filter.Contains(pos)) continue;

                // Already clone content: this is the copy we are keeping.
                if (gear.transform.IsChildOf(masterT)) continue;

                // A switched-off loose item is not what this is about - it is invisible,
                // so it is not the loot left hanging in the air - and it may be something
                // the game has deliberately parked. Only live copies are removed.
                if (!gear.gameObject.activeInHierarchy) continue;

                // NEVER touch anything on the player, in their hands or in their
                // inventory, and never anything under the game's own placement root -
                // that is where the player's belongings live.
                if (IsPlayerOrInventory(gear.transform)) continue;
                if (IsUnderPlacementRoot(gear.transform)) continue;

                // Another building's clone owns this one.
                if (BelongsToAnotherInstance(instance, gear.transform)) continue;

                bool isCopy = false;

                var looseGuid = gear.GetComponent<Il2Cpp.ObjectGuid>();
                if (looseGuid != null && !string.IsNullOrEmpty(looseGuid.m_Guid)
                    && ownedGuids.Contains(looseGuid.m_Guid))
                {
                    isCopy = true;
                }
                else if (HasOwnedCopyNear(ownedCells, CleanGearName(gear.gameObject.name), pos, sqrRadius))
                {
                    isCopy = true;
                }

                if (!isCopy) continue;

                // The contents of a container are not loose world gear and are restored
                // by RestoreContainerData, so they are off limits here.
                if (gear.GetComponentInParent<Il2Cpp.Container>() != null) continue;

                // Named for the first few, like [ADOPT] is: this line destroys objects, so
                // when an item goes missing it has to be possible to see it happen.
                if (purged < 10)
                    MelonLogger.Msg($"[GEAR-LEAK] {instance.Config.ResolvedInstanceId}: " +
                                    $"'{gear.gameObject.name}' kopyasi silindi (klonun kendi esyasi, oyunun kaydindan geri gelmis).");

                UnityEngine.Object.DestroyImmediate(gear.gameObject);
                purged++;
            }

            if (purged > 0)
            {
                // DestroyImmediate changed the hierarchy instantly, so the shared scan
                // cache is holding dead references.
                SceneScan.InvalidateVolatile();

                // Reported at normal level: this line DESTROYS objects, so when an item
                // goes missing it has to be possible to see that it happened here.
                MelonLogger.Msg($"[GEAR-LEAK] {instance.Config.ResolvedInstanceId}: oyunun kendi kaydindan gelen " +
                                $"{purged} kopya esya temizlendi (klonun disinda kalmis kopyalar).");
            }

            return purged;
        }

        private static long LeakCellKey(Vector3 p)
        {
            return PackCell(
                Mathf.FloorToInt(p.x / LEAKED_GEAR_MATCH_RADIUS),
                Mathf.FloorToInt(p.y / LEAKED_GEAR_MATCH_RADIUS),
                Mathf.FloorToInt(p.z / LEAKED_GEAR_MATCH_RADIUS));
        }

        // 3x3x3 cell neighbourhood, because a match can sit just across a cell border.
        private static bool HasOwnedCopyNear(Dictionary<long, List<DedupEntry>> cells,
                                             string name, Vector3 pos, float sqrRadius)
        {
            int bx = Mathf.FloorToInt(pos.x / LEAKED_GEAR_MATCH_RADIUS);
            int by = Mathf.FloorToInt(pos.y / LEAKED_GEAR_MATCH_RADIUS);
            int bz = Mathf.FloorToInt(pos.z / LEAKED_GEAR_MATCH_RADIUS);

            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        List<DedupEntry> bucket;
                        if (!cells.TryGetValue(PackCell(bx + dx, by + dy, bz + dz), out bucket)) continue;

                        foreach (var e in bucket)
                        {
                            if (e.name != name) continue;
                            if ((e.pos - pos).sqrMagnitude <= sqrRadius) return true;
                        }
                    }

            return false;
        }

        // ─────────────────────────────────────────────────────────────────
        // DEAD ENTRIES IN THE GAME'S GEAR REGISTRY
        //
        // The same defect FixPlaceableManagerSerializeInventoryPatch already fixes for
        // PlaceableManager, one registry over. The mod destroys clone GearItems in
        // several places (the rogue/guid/spatial de-duplication, the gear restore, the
        // loot roll). Any entry left behind for a destroyed item makes
        // GearManager.Serialize() walk into a dead object - and that blob is exactly
        // where the region's LOOSE gear lives: everything the player dropped outdoors.
        // One throw in there and the whole list is missing from the save, which from the
        // player's side looks like "I put things on the ground, went inside, came back
        // and they were gone".
        //
        // Pruning first cannot lose anything: an entry that answers null has no object
        // behind it any more.
        // ─────────────────────────────────────────────────────────────────
        public static int PruneDeadGearManagerEntries()
        {
            try
            {
                var list = Il2Cpp.GearManager.m_Gear;
                if (list == null) return 0;

                int removed = 0;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    // Only a null test: Unity reports a destroyed object as null, and
                    // touching any member of one would throw.
                    if (list[i] != null) continue;

                    list.RemoveAt(i);
                    removed++;
                }

                if (removed > 0)
                {
                    // The staggered per-frame update indexes into this list and the list
                    // is now shorter, so restart it from the beginning.
                    Il2Cpp.GearManager.m_CurrentIndex = 0;

                    MelonLogger.Warning($"[GEAR-FIX] GearManager.m_Gear icinde {removed} olu kayit vardi, " +
                                        $"kayit yazilmadan once temizlendi.");
                }

                return removed;
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[GEAR-FIX] m_Gear temizligi hatasi: {ex.Message}");
                return 0;
            }
        }
    }
}
