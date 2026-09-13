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

                // THE SAME RULE AS SetObjectVisualState, AND FOR THE SAME REASON.
                //
                // This repair exists for colliders the mod's own hide pass switched off
                // and failed to switch back on. It was written as "enable everything that
                // is off", which also switched on colliders the SCENE ships disabled - and
                // a safe whose MeshCollider is supposed to stay off became impossible to
                // click, because the game drives its interaction off the box collider and
                // the mesh was now in the way.
                if (!WeDisabled(c)) continue;

                c.enabled = true;
                ForgetDisabledCollider(c);
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

            DescribePlayerInteractionState();
            CensusSafes();
            DiagnoseCrosshair();

            if (instance != null)
                DiagnoseNearbyItems(instance, playerT.position);

            MelonLogger.Msg("──────── [TESHIS] son ────────");
        }

        // ─────────────────────────────────────────────────────────────────
        // WHAT DID THE GAME ITSELF PICK?
        //
        // The per-object report below says whether an interaction is WILLING. It does not
        // say whether the game ever OFFERED it, and those are two different failures with
        // the same symptom on screen. The safe in the clone answers CanInteract=True and
        // still cannot be clicked, which means the object is not the part that is
        // refusing - something between the crosshair and it is.
        //
        // PlayerManager.ActiveInteraction is the game's own answer to "what is the player
        // looking at right now". Read while aiming at the object, it splits the problem in
        // one line: NULL means the object was never chosen, and anything else means it was
        // chosen and the failure is further along.
        // ─────────────────────────────────────────────────────────────────
        private static void DescribePlayerInteractionState()
        {
            var pm = GameManager.GetPlayerManagerComponent();
            if (pm == null)
            {
                MelonLogger.Msg("[TESHIS] PlayerManager yok.");
                return;
            }

            string active = "NULL";
            try
            {
                var ai = pm.ActiveInteraction;
                if (ai != null)
                {
                    // Naming the OBJECT matters as much as naming the type: "the game
                    // picked a ContainerInteraction" and "the game picked the safe's
                    // ContainerInteraction" are different answers.
                    active = "var";
                    try
                    {
                        var bi = ai.TryCast<Il2CppTLD.Interactions.BaseInteraction>();
                        if (bi != null)
                            active = bi.GetIl2CppType().Name + " ('" + bi.gameObject.name + "')";
                    }
                    catch { }
                }
            }
            catch (System.Exception ex) { active = "HATA(" + ex.GetType().Name + ")"; }

            MelonLogger.Msg($"[TESHIS] Oyuncu: aktifEtkilesim={active} " +
                            $"nisangahaYakin={Ask(() => pm.IsInteractionNearCrosshair.ToString())} " +
                            $"kontrolModu={Ask(() => pm.GetControlMode().ToString())} " +
                            $"yerlestirilenObje={Ask(() => pm.m_ObjectToPlace == null ? "yok" : pm.m_ObjectToPlace.name)}");

            // The mod forces InCustomizableSafehouse to true inside every clone so that
            // decorating works there at all. If that is what is putting the crosshair on
            // the wrong interaction, it shows up here first.
            try
            {
                var sm = GameManager.GetSafehouseManager();
                MelonLogger.Msg(sm == null
                    ? "[TESHIS] SafehouseManager yok."
                    : $"[TESHIS] Safehouse: duzenlenebilir={Ask(() => sm.InCustomizableSafehouse().ToString())} " +
                      $"duzenlemeModu={Ask(() => sm.IsCustomizing().ToString())}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Msg($"[TESHIS] Safehouse durumu okunamadi: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // HOW MANY SAFES ARE THERE, ACTUALLY?
        //
        // The setup log answered the question it was asked - SafeCracking.Start is
        // entered AND left, so the safe's setup runs in full and the empty combination
        // and missing dial are normal for a safe nobody has started cracking - and it
        // showed something nobody was looking for:
        //
        //   SafeCracking.Awake 'CONTAINER_SafeB'
        //   SafeCracking.Awake 'CONTAINER_SafeB (1)'
        //
        // TWO of them. The locker that works has no twin. A clone is built by merging
        // the interior scene's variants (base / _SANDBOX / _DLC01) under one
        // MasterInterior, so an object present in more than one variant ends up in the
        // building twice, in the same place, both fully alive.
        //
        // Two overlapping interactive containers is a real reason for the crosshair to
        // settle on neither, and it fits the one result that never made sense: calling
        // PerformInteraction by hand works, because that skips the choosing entirely.
        //
        // This counts them and says where each one is. If the two sit on top of each
        // other, that is the bug; if they are metres apart, this is a second safe in the
        // building and the hunt continues.
        // ─────────────────────────────────────────────────────────────────
        private static void CensusSafes()
        {
            try
            {
                var safes = UnityEngine.Object.FindObjectsOfType<Il2Cpp.SafeCracking>(true);
                int count = safes == null ? 0 : safes.Length;

                MelonLogger.Msg($"[TESHIS] Sahnedeki SafeCracking sayisi: {count}");
                if (safes == null) return;

                foreach (var s in safes)
                {
                    if (s == null || s.gameObject == null) continue;

                    GameObject go = s.gameObject;

                    string owner = "(klon disi)";
                    try
                    {
                        var inst = FindInstanceOwning(go.transform);
                        if (inst != null) owner = inst.Config.ResolvedInstanceId;
                    }
                    catch { }

                    string locked = "?";
                    try
                    {
                        var c = go.GetComponent<Il2Cpp.Container>();
                        if (c != null) locked = c.IsSafeLocked().ToString();
                    }
                    catch { }

                    Vector3 p = Vector3.zero;
                    try { p = go.transform.position; } catch { }

                    MelonLogger.Msg($"[TESHIS]   kasa '{go.name}' sahip={owner} " +
                                    $"aktif={Ask(() => go.activeInHierarchy.ToString())} " +
                                    $"kilitli={locked} " +
                                    $"pos=({p.x:F2},{p.y:F2},{p.z:F2}) " +
                                    $"parent={Ask(() => go.transform.parent != null ? go.transform.parent.name : "ROOT")} " +
                                    $"sahne={Ask(() => go.scene.name)}");
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Msg($"[TESHIS] Kasa sayimi basarisiz: {ex.Message}");
            }
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

                if (i < DIAG_INTERACTION_HITS) DescribeInteractionState(go);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // WHY IS THIS OBJECT NOT INTERACTIVE?
        //
        // The collider report above answers one half of the question: can the crosshair's
        // ray reach the object at all. It cannot answer the other half - whether the game
        // is WILLING to offer an interaction - and for anything more complicated than a
        // loose item that is where the answer lives.
        //
        // A combination safe is three things at once: a Container, a SafeCracking, and a
        // ContainerInteraction that asks both of them whether it may run. Any one of the
        // three saying no leaves an object that looks exactly like a dead prop - solid,
        // visible, and impossible to click - which is indistinguishable from a missing
        // collider from the player's side of the screen, and was not distinguishable from
        // the log either.
        //
        // Every value is read on its own and reported even when it cannot be read: a line
        // saying which question threw is still an answer.
        // ─────────────────────────────────────────────────────────────────
        private const int DIAG_INTERACTION_HITS = 3;

        private static void DescribeInteractionState(GameObject go)
        {
            if (go == null) return;

            try
            {
                var g = go.GetComponentInParent<Il2Cpp.ObjectGuid>();
                if (g != null)
                    MelonLogger.Msg($"[TESHIS]      kimlik: guid={Safe(g.m_Guid)} pdid={Safe(g.PDID)}");
            }
            catch (System.Exception ex) { MelonLogger.Msg($"[TESHIS]      kimlik okunamadi: {ex.Message}"); }

            // ── Container ─────────────────────────────────────────────────
            Il2Cpp.Container container = null;
            try { container = go.GetComponentInParent<Il2Cpp.Container>(); }
            catch (System.Exception ex) { MelonLogger.Msg($"[TESHIS]      Container aranamadi: {ex.Message}"); }

            if (container != null)
            {
                // THE WHOLE COMPONENT LIST, ONCE.
                //
                // A safe and a locker are built the same way and behave differently, so
                // the difference is either in a value or in the parts list. Values have
                // been checked one at a time and all of them match; the parts list has
                // not been looked at at all. Printed side by side, one missing component
                // is the entire answer and needs no further round trip.
                DescribeComponents(container.gameObject);
                DescribeColliders(container.gameObject);
                DescribeHoverIcons(container.gameObject);
                DescribeXPModeGate(container.gameObject);

                MelonLogger.Msg($"[TESHIS]      Container '{container.gameObject.name}' " +
                                $"bilesenAcik={Ask(() => container.enabled.ToString())} " +
                                $"aktif={Ask(() => container.gameObject.activeInHierarchy.ToString())} " +
                                $"kilitli={Ask(() => container.IsLocked().ToString())} " +
                                $"kasaKilitli={Ask(() => container.IsSafeLocked().ToString())} " +
                                $"aslaAcilamaz={Ask(() => container.CanNeverBeOpened().ToString())} " +
                                $"kasaVar={Ask(() => (container.GetSafe() != null).ToString())} " +
                                $"kayittaPasif={Ask(() => container.m_MarkedInactiveInSaveData.ToString())}");
            }

            // ── SafeCracking ──────────────────────────────────────────────
            Il2Cpp.SafeCracking safe = null;
            try { safe = go.GetComponentInParent<Il2Cpp.SafeCracking>(); } catch { }
            if (safe == null && container != null) { try { safe = container.GetSafe(); } catch { } }

            if (safe != null)
            {
                // m_Tumblers / m_DialObject are built during Awake/Start. A safe whose
                // clone never got through those is the shape of failure this is looking
                // for: the component is there, the object is there, and every question
                // about its state comes back empty.
                MelonLogger.Msg($"[TESHIS]      SafeCracking '{safe.gameObject.name}' " +
                                $"bilesenAcik={Ask(() => safe.enabled.ToString())} " +
                                $"aktif={Ask(() => safe.gameObject.activeInHierarchy.ToString())} " +
                                $"kirilmis={Ask(() => safe.m_Cracked.ToString())} " +
                                $"acik={Ask(() => safe.IsOpen().ToString())} " +
                                $"zorluk={Ask(() => safe.m_Difficulty.ToString())} " +
                                $"tumblerSayisi={Ask(() => safe.m_NumTumblers.ToString())} " +
                                $"tumblerlar={Ask(() => (safe.m_Tumblers == null ? "NULL" : safe.m_Tumblers.Length.ToString()))} " +
                                $"kadran={Ask(() => (safe.m_DialObject == null ? "NULL" : "var"))} " +
                                $"openClose={Ask(() => (safe.m_OpenClose == null ? "NULL" : "var"))} " +
                                $"panelRef={Ask(() => (safe.m_SafeCracking == null ? "NULL" : "var"))} " +
                                $"sifre={Ask(() => (safe.m_Combination == null ? "NULL" : safe.m_Combination.Length.ToString()))} " +
                                // An empty combination on a safe that is SUPPOSED to roll one
                                // says its setup never finished - which is the first thing to
                                // suspect if the typed CanInteract below comes back false.
                                $"sifreUretilir={Ask(() => safe.m_RollCombination.ToString())} " +
                                $"kadranTik={Ask(() => safe.m_NumTicksOnDial.ToString())} " +
                                $"tamburSirasi={Ask(() => safe.m_CurrentTumblerIndex.ToString())}");
            }

            // ── The interactions themselves ───────────────────────────────
            try
            {
                var interactions = go.GetComponentsInParent<Il2CppTLD.Interactions.BaseInteraction>(true);
                if (interactions == null || interactions.Length == 0)
                {
                    MelonLogger.Msg("[TESHIS]      Etkilesim bileseni YOK (bu obje zaten tiklanabilir degil).");
                    return;
                }

                foreach (var it in interactions)
                {
                    if (it == null) continue;

                    string typeName = "BaseInteraction";
                    try { typeName = it.GetIl2CppType().Name; } catch { }

                    // HoverText is the CACHED text, and it is only filled while the
                    // interaction is the one the player is looking at - so on a broken
                    // object it is empty as a CONSEQUENCE of never being chosen, which
                    // says nothing about why. The container's own GetHoverText() is the
                    // question worth asking: an interaction that cannot produce a line of
                    // text is one the game has no way to offer.
                    //
                    // ASK THE REAL CLASS, NOT THE BASE ONE.
                    //
                    // ContainerInteraction declares its OWN IsEnabled and CanInteract.
                    // Reading them through a BaseInteraction-typed reference can answer
                    // with the base implementation instead of the override - which is
                    // exactly what "both the broken safe and the working locker say
                    // True" looks like. The typed reading below is the one that counts;
                    // where the two disagree, the base answer was never the truth.
                    string computed = "-", actionText = "-";
                    string ownEnabled = "-", ownCan = "-", ownActive = "-";

                    var ci = it.TryCast<Il2Cpp.ContainerInteraction>();
                    if (ci != null)
                    {
                        computed = Ask(() => ci.GetHoverText());
                        actionText = Ask(() => ci.GetInteractiveActionText());
                        ownEnabled = Ask(() => ci.IsEnabled.ToString());
                        ownCan = Ask(() => ci.CanInteract.ToString());
                        ownActive = Ask(() => ci.IsActive.ToString());
                    }

                    MelonLogger.Msg($"[TESHIS]      {typeName} '{Ask(() => it.gameObject.name)}' " +
                                    $"bilesenAcik={Ask(() => it.enabled.ToString())} " +
                                    $"[taban] IsEnabled={Ask(() => it.IsEnabled.ToString())} " +
                                    $"CanInteract={Ask(() => it.CanInteract.ToString())} " +
                                    $"| [kendi] IsEnabled={ownEnabled} CanInteract={ownCan} IsActive={ownActive} " +
                                    $"| metin='{Ask(() => it.HoverText)}' " +
                                    $"hesaplananMetin='{computed}' eylemMetni='{actionText}'");
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Msg($"[TESHIS]      Etkilesim bilesenleri okunamadi: {ex.Message}");
            }
        }

        private static void DescribeComponents(GameObject go)
        {
            if (go == null) return;

            try
            {
                var comps = go.GetComponents<Component>();
                if (comps == null) return;

                var names = new List<string>();
                foreach (var c in comps)
                {
                    if (c == null) { names.Add("(olu)"); continue; }

                    string n = "?";
                    try { n = c.GetIl2CppType().Name; } catch { }

                    // A disabled Behaviour is worth marking - "the component is there" and
                    // "the component is running" are different statements.
                    try
                    {
                        var b = c.TryCast<Behaviour>();
                        if (b != null && !b.enabled) n += "(kapali)";
                    }
                    catch { }

                    names.Add(n);
                }

                MelonLogger.Msg($"[TESHIS]      bilesenler ({names.Count}): {string.Join(", ", names.ToArray())}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Msg($"[TESHIS]      bilesen listesi okunamadi: {ex.Message}");
            }
        }

        // The safe carries a second collider the locker does not have (a BoxCollider next
        // to the MeshCollider), and the crosshair ray reports the object twice because of
        // it. Which of the two the game's own pick lands on is not something the hit list
        // above can show, so both are described here.
        private static void DescribeColliders(GameObject go)
        {
            if (go == null) return;

            try
            {
                var cols = go.GetComponents<Collider>();
                if (cols == null || cols.Length == 0) return;

                var parts = new List<string>();
                foreach (var c in cols)
                {
                    if (c == null) continue;

                    string t = "?";
                    try { t = c.GetIl2CppType().Name; } catch { }
                    parts.Add($"{t}(acik={Ask(() => c.enabled.ToString())},trigger={Ask(() => c.isTrigger.ToString())})");
                }

                MelonLogger.Msg($"[TESHIS]      collider'lar: {string.Join(", ", parts.ToArray())}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Msg($"[TESHIS]      collider listesi okunamadi: {ex.Message}");
            }
        }

        // "No icon, no marker, no interaction at all" is the report, and this component is
        // literally the list of icons the object asks the HUD to show. An empty list here
        // would be that sentence in data.
        private static void DescribeHoverIcons(GameObject go)
        {
            if (go == null) return;

            try
            {
                var h = go.GetComponent<Il2Cpp.HoverIconsToShow>();
                if (h == null)
                {
                    MelonLogger.Msg("[TESHIS]      HoverIconsToShow YOK.");
                    return;
                }

                var icons = h.m_HoverIcons;
                if (icons == null || icons.Length == 0)
                {
                    MelonLogger.Msg("[TESHIS]      HoverIconsToShow: ikon listesi BOS.");
                    return;
                }

                var names = new List<string>();
                for (int i = 0; i < icons.Length; i++) names.Add(icons[i].ToString());

                MelonLogger.Msg($"[TESHIS]      HoverIconsToShow ({icons.Length}): {string.Join(", ", names.ToArray())}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Msg($"[TESHIS]      HoverIconsToShow okunamadi: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // F11 - RUN THE INTERACTION THE GAME WILL NOT OFFER
        //
        // Everything about the safe reads healthy: the collider is reachable, the
        // Container is open for business, ContainerInteraction says IsEnabled and
        // CanInteract and produces a hover line ("Kasayi arastir"). And the game's own
        // pick for the crosshair is still NULL.
        //
        // That leaves exactly two possibilities and no reading can tell them apart:
        // either the SELECTION step never chooses this object, or the interaction itself
        // would fail if it were chosen. So it is chosen by hand. What happens next is the
        // answer - the safe opens and the selection is the only thing at fault, or
        // nothing happens and the fault is in the interaction after all.
        //
        // This performs the same call the game makes on a click, and nothing else.
        // ─────────────────────────────────────────────────────────────────
        public static void ForceCrosshairInteraction()
        {
            MelonLogger.Msg("──────── [ZORLA] Nisangahtaki etkilesim ────────");

            var cam = GameManager.GetMainCamera();
            if (cam == null) { MelonLogger.Msg("[ZORLA] Ana kamera yok."); return; }

            RaycastHit[] hits;
            try
            {
                hits = Physics.RaycastAll(new Ray(cam.transform.position, cam.transform.forward),
                                          DIAG_RADIUS, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            }
            catch (System.Exception ex) { MelonLogger.Msg($"[ZORLA] Isin atilamadi: {ex.Message}"); return; }

            if (hits == null || hits.Length == 0) { MelonLogger.Msg("[ZORLA] Nisangahin onunde collider yok."); return; }

            var sorted = new List<RaycastHit>();
            foreach (var h in hits) sorted.Add(h);
            sorted.Sort(delegate (RaycastHit a, RaycastHit b) { return a.distance.CompareTo(b.distance); });

            foreach (var hit in sorted)
            {
                if (hit.collider == null) continue;

                Il2Cpp.ContainerInteraction ci = null;
                try { ci = hit.collider.gameObject.GetComponentInParent<Il2Cpp.ContainerInteraction>(); }
                catch { }

                if (ci == null) continue;

                MelonLogger.Msg($"[ZORLA] '{Ask(() => ci.gameObject.name)}' uzerinde PerformInteraction cagriliyor " +
                                $"(mesafe={hit.distance:F2}m, CanInteract={Ask(() => ci.CanInteract.ToString())}).");

                try
                {
                    bool ok = ci.PerformInteraction();
                    MelonLogger.Msg($"[ZORLA] PerformInteraction sonucu: {ok}");
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"[ZORLA] PerformInteraction hata verdi: {ex}");
                }

                MelonLogger.Msg("──────── [ZORLA] son ────────");
                return;
            }

            MelonLogger.Msg("[ZORLA] Nisangahin onunde ContainerInteraction tasiyan bir obje yok.");
            MelonLogger.Msg("──────── [ZORLA] son ────────");
        }

        // THE SAFE CARRIES A SWITCH THE LOCKER DOES NOT.
        //
        // DisableObjectForXPMode is the game saying "this object does not exist on some
        // difficulties", and this playthrough is Stalker. The component sits on the safe
        // with its own enabled flag already false, which is what a component that has
        // done its job and stepped aside looks like - and whatever it did, it did in the
        // ORIGINAL scene, before the mod cloned anything.
        //
        // The mod then switches the clone's objects back on in several places without
        // knowing why they were off. A safe the game meant to remove for this difficulty
        // would come back visible and solid, with the thing that was supposed to keep it
        // out of the interaction system no longer running. That is exactly the shape of
        // "it is there, it answers every question correctly, and the game will not look
        // at it".
        private static void DescribeXPModeGate(GameObject go)
        {
            if (go == null) return;

            try
            {
                string mode = Ask(() => Il2Cpp.ExperienceModeManager.GetCurrentExperienceModeType().ToString());

                var gate = go.GetComponent<Il2Cpp.DisableObjectForXPMode>();
                if (gate == null)
                {
                    MelonLogger.Msg($"[TESHIS]      XP modu={mode}, DisableObjectForXPMode YOK.");
                    return;
                }

                string modes = "(okunamadi)";
                try
                {
                    var list = gate.m_XPModesToDisable;
                    if (list == null) modes = "NULL";
                    else
                    {
                        var names = new List<string>();
                        for (int i = 0; i < list.Count; i++) names.Add(list[i].ToString());
                        modes = names.Count == 0 ? "(bos)" : string.Join("/", names.ToArray());
                    }
                }
                catch { }

                MelonLogger.Msg($"[TESHIS]      XP modu={mode} | DisableObjectForXPMode " +
                                $"bilesenAcik={Ask(() => gate.enabled.ToString())} " +
                                $"buModaKapatilmali={Ask(() => gate.ShouldDisableForCurrentMode().ToString())} " +
                                $"kapatilacakModlar={modes} " +
                                $"storyAyri={Ask(() => gate.m_HandleStoryModesSeparately.ToString())}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Msg($"[TESHIS]      XP modu kapisi okunamadi: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // ─────────────────────────────────────────────────────────────────
        // THE "PRETEND IT IS ALREADY CRACKED" EXPERIMENT (F11)
        //
        // WHAT IS LEFT AFTER EVERYTHING ELSE. The safe has been measured against a
        // working locker one metre away and every difference has been eliminated:
        // distance, layer, colliders, the XP-mode gate, the hover-icon list, the
        // base-vs-override readings, and the duplicate twin (which turned out to be the
        // scene's own inactive Nightmare variant, not something the mod made). Both
        // Awake and Start run to completion on it. Calling PerformInteraction by hand
        // returns true. The game still never picks it.
        //
        // The ONLY thing about the safe that the locker does not have is SafeCracking,
        // and the state it produces: Container.IsSafeLocked() is true. So that is the
        // last untested variable, and this flips it directly.
        //
        // m_Cracked is real save state, not a display flag - this is a deliberate change
        // to the safe, which is why it toggles back on a second press and why it belongs
        // on a throwaway test save. If the prompt appears while it is cracked, the answer
        // is that the game refuses to offer a LOCKED safe through this path at all, and
        // the missing piece is whatever offers the cracking interaction instead.
        // ─────────────────────────────────────────────────────────────────
        private static Il2Cpp.SafeCracking s_CrackExperiment;

        public static void ToggleSafeCrackedExperiment()
        {
            MelonLogger.Msg("──────── [DENEY] Kasa kirilmis gibi davran ────────");

            // Second press: put the safe back exactly as it was.
            if (s_CrackExperiment != null)
            {
                try
                {
                    s_CrackExperiment.m_Cracked = false;
                    MelonLogger.Msg($"[DENEY] '{Ask(() => s_CrackExperiment.gameObject.name)}' tekrar kilitli yapildi, deney bitti.");
                }
                catch (System.Exception ex) { MelonLogger.Warning($"[DENEY] Geri alinamadi: {ex.Message}"); }

                s_CrackExperiment = null;
                MelonLogger.Msg("──────── [DENEY] son ────────");
                return;
            }

            var cam = GameManager.GetMainCamera();
            if (cam == null) { MelonLogger.Msg("[DENEY] Ana kamera yok."); return; }

            RaycastHit[] hits;
            try
            {
                hits = Physics.RaycastAll(new Ray(cam.transform.position, cam.transform.forward),
                                          DIAG_RADIUS, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            }
            catch (System.Exception ex) { MelonLogger.Msg($"[DENEY] Isin atilamadi: {ex.Message}"); return; }

            if (hits == null || hits.Length == 0) { MelonLogger.Msg("[DENEY] Nisangahin onunde collider yok."); return; }

            var sorted = new List<RaycastHit>();
            foreach (var h in hits) sorted.Add(h);
            sorted.Sort(delegate (RaycastHit a, RaycastHit b) { return a.distance.CompareTo(b.distance); });

            Il2Cpp.SafeCracking safe = null;
            foreach (var hit in sorted)
            {
                if (hit.collider == null) continue;
                try { safe = hit.collider.gameObject.GetComponentInParent<Il2Cpp.SafeCracking>(); }
                catch { }
                if (safe != null) break;
            }

            if (safe == null)
            {
                MelonLogger.Msg("[DENEY] Nisangahin onunde kasa yok.");
                MelonLogger.Msg("──────── [DENEY] son ────────");
                return;
            }

            try
            {
                safe.m_Cracked = true;
                s_CrackExperiment = safe;
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[DENEY] m_Cracked yazilamadi: {ex.Message}");
                MelonLogger.Msg("──────── [DENEY] son ────────");
                return;
            }

            string nowLocked = "?";
            try
            {
                var c = safe.gameObject.GetComponent<Il2Cpp.Container>();
                if (c != null) nowLocked = c.IsSafeLocked().ToString();
            }
            catch { }

            MelonLogger.Msg($"[DENEY] '{Ask(() => safe.gameObject.name)}' kirilmis sayildi. " +
                            $"Container.IsSafeLocked artik={nowLocked}. " +
                            "SIMDI KASAYA BAK: yazi/ikon cikiyor mu? Tekrar F11 = geri al.");
            MelonLogger.Msg("──────── [DENEY] son ────────");
        }


        // A value that will not be read is still worth a word in the report.
        private static string Ask(System.Func<string> read)
        {
            try
            {
                string v = read();
                return string.IsNullOrEmpty(v) ? "(bos)" : v;
            }
            catch (System.Exception ex) { return "HATA(" + ex.GetType().Name + ")"; }
        }

        private static string Safe(string s)
        {
            return string.IsNullOrEmpty(s) ? "(yok)" : s;
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
