using Il2Cpp;
using MelonLoader;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─────────────────────────────────────────────────────────────────
        // CLONE SCENE ITEM INTERACTION (collider) REPAIR AND DIAGNOSTICS
        //
        // PROBLEM: while a clone scene is hidden, SetObjectVisualState disables BOTH
        // the Renderers AND the Colliders of every GearItem / Placeable
        // (see SeamlessInteriorsMod.Spawning.cs). But the way back is NOT THE SAME
        // for the two:
        //
        //   - Renderers are re-enabled UNCONDITIONALLY in THREE places:
        //       * end of Run() (Main.cs)
        //       * TryBatchUpdateEnvironment (Environment.cs)
        //       * on walking through a door (PortalPatches)
        //     All of these say "enable EVERY Renderer under MasterInterior".
        //
        //   - Colliders were re-enabled ONLY inside SetInteriorItemsVisible(instance,
        //     true), and that path skipped any item not activeInHierarchy at the time.
        //
        // Result: once an item's collider stays off (another instance thought the item
        // was in its own volume and disabled it, or its hierarchy was inactive at the
        // moment of re-enabling), one of the bulk renderer paths still makes it
        // VISIBLE, but with the collider off the player's crosshair never hits it: the
        // item can be seen, yet not picked up and not selectable in placement (Y) mode.
        //
        // FIX: re-enable colliders as unconditionally as renderers, plus a safety net
        // that periodically repairs the clone the player is standing in.
        // ─────────────────────────────────────────────────────────────────

        private static int ReenableCollidersOn(GameObject go)
        {
            if (go == null) return 0;

            int count = 0;
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
            {
                if (c == null || c.enabled) continue;
                c.enabled = true;
                count++;
            }
            return count;
        }

        public static int RestoreInteriorItemColliders(SeamlessInteriorInstance instance, bool onlyActive = false)
        {
            if (instance == null || instance.MasterInterior == null) return 0;

            int repaired = 0;

            foreach (var gear in instance.MasterInterior.GetComponentsInChildren<Il2Cpp.GearItem>(true))
            {
                if (gear == null || gear.gameObject == null) continue;
                if (onlyActive && !gear.gameObject.activeInHierarchy) continue;
                if (PlayerRefs.IsPlayerRoot(gear.transform.root)) continue;   // never touch carried gear

                repaired += ReenableCollidersOn(gear.gameObject);
            }

            foreach (var p in instance.MasterInterior.GetComponentsInChildren<Il2CppTLD.Placement.Placeable>(true))
            {
                if (p == null || p.gameObject == null) continue;
                if (onlyActive && !p.gameObject.activeInHierarchy) continue;
                if (PlayerRefs.IsPlayerRoot(p.transform.root)) continue;

                repaired += ReenableCollidersOn(p.gameObject);
            }

            return repaired;
        }

        public static SeamlessInteriorInstance GetInstancePlayerIsIn()
        {
            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT == null) return null;

            Vector3 pos = playerT.position;
            foreach (var instance in ActiveInteriors.Values)
            {
                if (!instance.RunCompleted || instance.MasterInterior == null) continue;
                if (!instance.MasterInterior.activeSelf) continue;
                // Trigger test first, then the slightly padded volume test as backup.
                if (instance.IsPositionInside(pos) || instance.IsPositionInVolume(pos, 0.5f))
                    return instance;
            }
            return null;
        }

        // Safety net: periodically repair colliders left disabled while the player is
        // inside. Runs rarely because GetComponentsInChildren scans are expensive.
        public const float INTERACTIVITY_REPAIR_INTERVAL = 2.0f;

        public static int RepairInteractivityForPlayerInstance(bool verbose = false)
        {
            var instance = GetInstancePlayerIsIn();
            if (instance == null)
            {
                if (verbose) MelonLogger.Msg("[ETKILESIM-ONARIM] Oyuncu bir klon sahnenin icinde degil.");
                return 0;
            }

            int repaired = RestoreInteriorItemColliders(instance, true);

            if (repaired > 0 || verbose)
            {
                MelonLogger.Msg($"[ETKILESIM-ONARIM] {instance.Config.ResolvedInstanceId}: " +
                                $"{repaired} kapali collider geri acildi.");
            }

            return repaired;
        }

        // ─────────────────────────────────────────────────────────────────
        // DIAGNOSTICS (F9)
        //
        // Answers "why is this item not interactive?" in a single log dump: every
        // hit in front of the crosshair, plus the active / collider / layer state of
        // the items around the player.
        // ─────────────────────────────────────────────────────────────────

        private const float DIAG_RADIUS = 4.0f;

        public static void DiagnoseInteractivity()
        {
            MelonLogger.Msg("──────── [TESHIS] Esya etkilesimi ────────");

            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT == null)
            {
                MelonLogger.Msg("[TESHIS] Oyuncu bulunamadi.");
                return;
            }

            var instance = GetInstancePlayerIsIn();
            MelonLogger.Msg(instance == null
                ? "[TESHIS] Oyuncu HICBIR klon sahnenin icinde gorunmuyor."
                : $"[TESHIS] Bulunulan klon: {instance.Config.ResolvedInstanceId}");

            DiagnoseCrosshair();

            if (instance != null)
                DiagnoseNearbyItems(instance, playerT.position);

            MelonLogger.Msg("──────── [TESHIS] son ────────");
        }

        private static void DiagnoseCrosshair()
        {
            var cam = GameManager.GetMainCamera();
            if (cam == null)
            {
                MelonLogger.Msg("[TESHIS] Ana kamera bulunamadi, nisangah testi atlandi.");
                return;
            }

            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            RaycastHit[] hits = Physics.RaycastAll(ray, DIAG_RADIUS, Physics.AllLayers, QueryTriggerInteraction.Collide);

            if (hits == null || hits.Length == 0)
            {
                MelonLogger.Msg($"[TESHIS] Nisangahin onunde {DIAG_RADIUS}m icinde HICBIR collider yok. " +
                                "(Bakilan esyanin collider'i kapali olabilir — asagidaki listeye bak.)");
                return;
            }

            // Sort near to far so the first blocker is obvious.
            var sorted = new List<RaycastHit>();
            foreach (var h in hits) sorted.Add(h);
            sorted.Sort(delegate (RaycastHit a, RaycastHit b) { return a.distance.CompareTo(b.distance); });

            MelonLogger.Msg($"[TESHIS] Nisangah isini {sorted.Count} collider'a carpti (yakindan uzaga):");
            for (int i = 0; i < sorted.Count; i++)
            {
                var col = sorted[i].collider;
                if (col == null) continue;

                GameObject go = col.gameObject;
                bool hasGear = go.GetComponentInParent<Il2Cpp.GearItem>() != null;
                bool hasPlaceable = go.GetComponentInParent<Il2CppTLD.Placement.Placeable>() != null;

                MelonLogger.Msg($"[TESHIS]   {i + 1}. {go.name} d={sorted[i].distance:F2}m " +
                                $"layer={LayerMask.LayerToName(go.layer)}({go.layer}) " +
                                $"trigger={col.isTrigger} gear={hasGear} placeable={hasPlaceable} " +
                                $"parent={(go.transform.parent != null ? go.transform.parent.name : "ROOT")}");
            }
        }

        private static void DiagnoseNearbyItems(SeamlessInteriorInstance instance, Vector3 playerPos)
        {
            int total = 0, brokenCount = 0;

            foreach (var gear in instance.MasterInterior.GetComponentsInChildren<Il2Cpp.GearItem>(true))
            {
                if (gear == null || gear.gameObject == null) continue;
                if (Vector3.Distance(gear.transform.position, playerPos) > DIAG_RADIUS) continue;

                total++;

                GameObject go = gear.gameObject;
                int colTotal = 0, colOn = 0;
                foreach (var c in go.GetComponentsInChildren<Collider>(true))
                {
                    if (c == null) continue;
                    colTotal++;
                    if (c.enabled) colOn++;
                }

                int rendTotal = 0, rendOn = 0;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    rendTotal++;
                    if (r.enabled) rendOn++;
                }

                // Visible but untouchable is exactly the failure mode described above.
                bool visible = go.activeInHierarchy && rendOn > 0;
                bool touchable = go.activeInHierarchy && colOn > 0;
                bool broken = visible && !touchable;
                if (broken) brokenCount++;

                string placeableState = "yok";
                var p = go.GetComponent<Il2CppTLD.Placement.Placeable>();
                if (p != null)
                {
                    try { placeableState = Il2CppTLD.Placement.PlaceableManager.GetPlacementState(p).ToString(); }
                    catch { placeableState = "okunamadi"; }
                    if (p.m_Invalidated) placeableState += " (invalidated)";
                }

                MelonLogger.Msg($"[TESHIS]   {(broken ? ">>> COLLIDER KAPALI <<< " : "")}{go.name} " +
                                $"aktif={go.activeInHierarchy} collider={colOn}/{colTotal} renderer={rendOn}/{rendTotal} " +
                                $"layer={LayerMask.LayerToName(go.layer)}({go.layer}) " +
                                $"gearComp={gear.enabled} placeable={placeableState}");
            }

            MelonLogger.Msg($"[TESHIS] {DIAG_RADIUS}m icinde {total} esya var, {brokenCount} tanesi GORUNUYOR AMA COLLIDER'I KAPALI.");
            MelonLogger.Msg("[TESHIS] F10 = bu klondaki kapali collider'lari aninda onar.");
        }
    }
}
