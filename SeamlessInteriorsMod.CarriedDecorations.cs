using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─────────────────────────────────────────────────────────────────────
        // DECORATIONS THE PLAYER IS CARRYING
        //
        // THE BUG THIS EXISTS FOR
        // Pick a decoration up with the safehouse feature - a deer head, a wall safe, a
        // calendar - then walk into an ORIGINAL interior (one with a loading screen) and
        // back out. The things in the backpack are suddenly standing around the player's
        // feet, moving with them, and stay there until the next save/load. Nothing is
        // lost: the backpack still holds them, and one save/load puts it right for good.
        //
        // WHY THE TRIP THROUGH A REAL INTERIOR IS WHAT TRIGGERS IT
        // The region scene is thrown away and rebuilt, so the mod re-creates every
        // building from its own files. _spawn_placeables.json was written at the LAST
        // SAVE - before the pickup - so it still describes those decorations as standing
        // in the building, and RestoreSpawnedPlaceables hands each record to the game's
        // Placeable.FindOrCreateAndDeserialize.
        //
        // For a guid the player is now carrying that call is the whole problem. The game
        // does not keep a scene object for a carried decoration, so "FindOrCreate" CREATES
        // one, and the placement registry - which does know the guid is OnPlayer - takes
        // the new object and puts it on the player. The record says it was switched on, so
        // switched on is what it stays: a real, visible object parented to the player.
        //
        // The next save rewrites _spawn_placeables.json from what is actually standing in
        // the building, the carried ones are no longer in it, and the whole thing stops -
        // which is exactly the "one save/load and it is fixed" the bug report describes.
        //
        // THE ANSWER COMES FROM THE GAME, NOT FROM THE HIERARCHY
        // Everywhere else the mod asks "is this object on the player?" by walking its
        // parents (IsPlayerOrInventory). That question cannot be asked here, because at
        // the moment it matters THERE IS NO OBJECT YET - only a guid in a file. So this
        // asks the placement registry instead, which is the one place that knows a guid
        // is in the backpack whether or not anything has been built for it.
        // ─────────────────────────────────────────────────────────────────────

        // ─────────────────────────────────────────────────────────────────────
        // READ THE REGISTRY ONCE, ANSWER FROM A SET
        //
        // THIS CRASHED THE GAME, so the shape of it matters as much as the answer.
        //
        // The first version asked the registry per object - PlaceableManager
        // .GetPlacementState(placeable), and failing that the dictionary's own
        // TryGetValue. RestorePlaceablePositions calls this once for every placeable in
        // a building, a hundred times for one door, and it calls it on THE CLONE'S OWN
        // placeables: objects the mod deliberately unregisters from the placement
        // registry and marks invalidated (ApplySpawnedPlaceable, InvalidateInteriorPlaceables).
        // That is an input the game's own code never produces - it never asks the
        // registry about something it has been told to forget - and the process died
        // inside it, taking the game with it. No managed exception, nothing in the log
        // past "aninda dolduruluyor": a native crash, which no try/catch here can hold.
        //
        // So nothing is asked ABOUT an object any more. The registry is walked - the one
        // operation on it this mod already does elsewhere and knows to be safe - and the
        // guids that come back are put in a plain managed set. The per-object question is
        // then a HashSet lookup with no interop in it at all, which is also what a
        // hundred-object loop should have been doing in the first place.
        // ─────────────────────────────────────────────────────────────────────
        private const float CARRIED_GUID_CACHE_SECONDS = 0.5f;

        private static readonly System.Collections.Generic.HashSet<string> s_CarriedGuids =
            new System.Collections.Generic.HashSet<string>();
        private static float s_CarriedGuidsBuiltAt = -999f;

        private static void RefreshCarriedGuids()
        {
            float now = Time.realtimeSinceStartup;
            if (now - s_CarriedGuidsBuiltAt < CARRIED_GUID_CACHE_SECONDS) return;
            s_CarriedGuidsBuiltAt = now;

            s_CarriedGuids.Clear();

            try
            {
                var dict = Il2CppTLD.Placement.PlaceableManager.s_Placeables;
                if (dict == null) return;

                foreach (var entry in dict)
                {
                    var info = entry.Value;
                    if (info == null) continue;
                    if (info.m_State != Il2CppTLD.Placement.PlacementState.OnPlayer) continue;

                    string key = entry.Key;
                    if (!string.IsNullOrEmpty(key)) s_CarriedGuids.Add(key);
                }
            }
            catch (System.Exception ex)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[TASINAN-DEKOR] Yerlestirme kaydi okunamadi: {ex.Message}");
            }
        }

        // Is the player carrying this decoration right now?
        public static bool IsCarriedDecoration(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return false;

            RefreshCarriedGuids();
            return s_CarriedGuids.Contains(guid);
        }

        public static bool IsCarriedDecoration(Il2CppTLD.Placement.Placeable p)
        {
            if (p == null) return false;

            // The object is standing on the player: nothing needs to be asked of anyone.
            try { if (PlayerRefs.IsPlayerRoot(p.transform.root)) return true; }
            catch { }

            string guid = null;
            try { guid = p.m_Guid; }
            catch { return false; }

            return IsCarriedDecoration(guid);
        }

        // ─────────────────────────────────────────────────────────────────────
        // THE INVARIANT, ENFORCED
        //
        // A decoration in the backpack must not also be an object standing in the world.
        // The restore above is the path that was proven to break that, but it is not the
        // only code that can switch a placeable on - the mod re-enables every renderer
        // under a building in half a dozen places, and the game has its own opinions
        // during a scene restore.
        //
        // So the rule is also checked rather than only argued for. It costs one walk of
        // the placement registry per second and reads two fields per entry; nothing is
        // scanned in the scene, because the registry already holds the object.
        //
        // WHAT IS DELIBERATELY LEFT ALONE
        //   - The object the player is currently positioning (PlayerManager.m_ObjectToPlace).
        //     That one is SUPPOSED to be visible - it is the placement ghost.
        //   - Everything, while the game is deserializing placements or restoring a save.
        //     The registry and the objects legitimately disagree in that window.
        //
        // Only the GameObject is switched off. Renderers and colliders are not touched:
        // the game turns the object back on through its own path when the decoration is
        // placed again, and an object put back with its renderers left disabled would be
        // solid and invisible - a worse bug than the one being fixed.
        // ─────────────────────────────────────────────────────────────────────
        private const float CARRIED_DECORATION_CHECK_INTERVAL = 1f;
        private static float s_NextCarriedDecorationCheck;

        // Named in the log for the first few, then counted. This hides objects the player
        // can see, so it has to be possible to tell that it happened.
        private const int CARRIED_DECORATION_LOG_LIMIT = 10;
        private static int s_CarriedDecorationsHidden;

        // Reused so the once-a-second check allocates nothing while there is nothing wrong,
        // which is the whole game except for the few seconds after a region rebuild.
        private static readonly System.Collections.Generic.List<GameObject> s_CarriedToHide =
            new System.Collections.Generic.List<GameObject>();

        // TWO CHECKS HAVE TO AGREE.
        //
        // Taking a decoration back OUT of the backpack switches the object on and changes
        // its state, and those are two separate operations - a check landing between them
        // sees "carried, yet standing in the world" for a moment. Hiding on that reading
        // would make the decoration the player just put down disappear as they place it.
        //
        // Nothing is hidden until the same object has looked wrong on two checks a second
        // apart, which no transition survives.
        private static readonly System.Collections.Generic.HashSet<int> s_CarriedSeenWrong =
            new System.Collections.Generic.HashSet<int>();
        private static readonly System.Collections.Generic.HashSet<int> s_CarriedSeenWrongPrev =
            new System.Collections.Generic.HashSet<int>();

        public static void TickCarriedDecorationGuard()
        {
            float now = Time.realtimeSinceStartup;
            if (now < s_NextCarriedDecorationCheck) return;
            s_NextCarriedDecorationCheck = now + CARRIED_DECORATION_CHECK_INTERVAL;

            try
            {
                if (Il2CppTLD.Placement.PlaceableManager.s_IsDeserializing) return;
                if (SaveGameSystem.IsRestoreInProgress() || SaveGameSystem.IsSceneRestoreInProgress()) return;
            }
            catch { return; }

            GameObject beingPlaced = null;
            try
            {
                var pm = GameManager.GetPlayerManagerComponent();
                if (pm != null) beingPlaced = pm.m_ObjectToPlace;
            }
            catch { }

            // COLLECT FIRST, SWITCH OFF AFTER. Placeable.OnDisable runs the moment the
            // object goes inactive and the placement registry is its own business - a
            // dictionary that changes while it is being walked throws, and that throw
            // would land in the middle of OnUpdate.
            s_CarriedToHide.Clear();
            s_CarriedSeenWrong.Clear();

            try
            {
                var dict = Il2CppTLD.Placement.PlaceableManager.s_Placeables;
                if (dict == null) return;

                foreach (var entry in dict)
                {
                    var info = entry.Value;
                    if (info == null) continue;
                    if (info.m_State != Il2CppTLD.Placement.PlacementState.OnPlayer) continue;

                    var handle = info.m_Handle;
                    if (handle == null || handle.gameObject == null) continue;

                    GameObject go = handle.gameObject;
                    if (!go.activeInHierarchy) continue;
                    if (beingPlaced != null && go == beingPlaced) continue;

                    int id = go.GetInstanceID();
                    s_CarriedSeenWrong.Add(id);

                    // First sighting: remembered, not acted on.
                    if (s_CarriedSeenWrongPrev.Contains(id)) s_CarriedToHide.Add(go);
                }
            }
            catch (System.Exception ex)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[TASINAN-DEKOR] Kontrol basarisiz: {ex.Message}");
                return;
            }

            s_CarriedSeenWrongPrev.Clear();
            foreach (int id in s_CarriedSeenWrong) s_CarriedSeenWrongPrev.Add(id);

            foreach (var go in s_CarriedToHide)
            {
                if (go == null) continue;

                try { go.SetActive(false); }
                catch (System.Exception ex)
                {
                    MelonLogger.Warning($"[TASINAN-DEKOR] '{go.name}' gizlenemedi: {ex.Message}");
                    continue;
                }

                if (s_CarriedDecorationsHidden < CARRIED_DECORATION_LOG_LIMIT)
                    MelonLogger.Msg($"[TASINAN-DEKOR] '{go.name}' cantada oldugu halde dunyada duruyordu, gizlendi.");

                s_CarriedDecorationsHidden++;
            }

            s_CarriedToHide.Clear();
        }
    }
}
