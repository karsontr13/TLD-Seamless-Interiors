using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── THE SOUND ENGINE'S IDEA OF WHERE THE PLAYER IS ───
        //
        // The sound engine mixes everything by two global states, Scene and InteriorStates. The game
        // sets them from AkState components: a region carries one that sets Scene to its own value
        // when the region starts (Forsaken Airfield: sceneEXT_01_Airfield), and an interior scene
        // carries ones that set Scene to the interior and InteriorStates to "int".
        //
        // With this mod loaded the interior's values are in force outdoors. The likely cause, not
        // confirmed: the mod loads the region's interior scenes in order to build its rooms, so their
        // components fire while the player is standing outdoors, and the last one to fire wins.
        // Measured 2026-10-11, Forsaken Airfield, a save loaded outdoors that had entered no room:
        // Scene read sceneINT_01_AFHangar and InteriorStates read interiorState_int, for the whole
        // region. The outdoor wind lost its main layer. Setting both back by hand restored it, by the
        // owner's ear, and the wind then dropped correctly on walking into the hangar under the
        // occlusion filter this mod already applies.
        //
        // So the rule here is the one the game's in-world buildings follow: the region's own states
        // stay in force, indoors and out, and the occlusion filter is what says "indoors". The
        // region's Scene value is read from the region's own component rather than from a table.
        //
        // WHAT THIS DOES NOT COVER: a region whose own Scene component cannot be told apart from an
        // interior's is left alone and named once in the log.

        // AkTriggerHandler's id for "fire when the component starts".
        private const int SoundTriggerOnStart = 1281810935;

        // Play_WindIndoors and Play_WindOutdoors.
        private const uint IndoorWindEvent = 3922582288;
        private const uint OutdoorWindEvent = 2660556499;
        // How long the switch at the door takes, in milliseconds. Not constants, so they can be
        // set while the game runs. On the way in the outdoors goes quickly. On the way out it comes
        // back slower: at 250 the owner found it abrupt (2026-10-12, "I don't think vanilla is
        // like that. It has a short fade-in").
        private static int s_OutdoorsFadeOutMs = 250;
        private static int s_OutdoorsFadeInMs = 1000;
        private static int s_IndoorWindFadeOutMs = 1000;
        // The old rule, kept only so the two can be compared in the running game.
        private static bool s_PauseOutdoorSoundsInside = false;

        private static float s_NextRoomSoundCheckTime = 0f;
        private static bool s_SoundSideWasInside = false;
        private static float s_SoundSideSince = 0f;
        private static bool s_RoomsStillLoading = false;
        private const float LoadingSoundCheckSeconds = 20f;
        private static string s_SoundStateRegion = null;
        private static uint s_RegionSceneState = 0;
        private static int s_RegionSceneStateSearches = 0;
        private static int s_SoundStateCorrections = 0;
        private static uint s_PausedOutdoorWindId = 0;
        private static bool s_WindLoopSideInside = false;
        private static int s_WindLoopFailures = 0;

        public static void TickRoomSound()
        {
            BringSoundInSlowlyAfterTheBlackScreen();

            PutPausesBackAfterTheGameResumesAudio();

            if (s_IsPlayerInsideClone != s_SoundSideWasInside)
            {
                s_SoundSideWasInside = s_IsPlayerInsideClone;
                s_SoundSideSince = Time.unscaledTime;
            }

            if (Time.unscaledTime < s_NextRoomSoundCheckTime) return;
            s_NextRoomSoundCheckTime = Time.unscaledTime + 1f;

            try
            {
                if (GameAudioManager.Instance == null) return;
                // The region is the scene the game really has loaded, never a loading-screen interior
                // reached from it: only a region has rooms of this mod's listed under its name.
                string region = ReadReal(() => GameManager.m_ActiveScene);
                if (string.IsNullOrEmpty(region)) return;

                // Before the wait below, on purpose: a save loaded inside a room flags the player
                // inside several seconds before the region's rooms are all built, and the outdoors
                // must not be heard behind this mod's loading screen either (the owner, 2026-10-12:
                // the flag "was still audible until the mod's black Loading screen ended"; the log
                // had the inside flag at 10:05:36 and the pause at 10:05:43).
                //
                // While the rooms are still being built the check runs every frame and pauses with
                // no fade: the region's sources start a few at a time over the first seconds of a
                // load, and at one check a second each was heard for up to a second behind the black
                // screen (the owner, 2026-10-12, build bea0910: "Some outdoor sounds were still
                // audible on the black loading screen"; the log had pauses of 4, 35 and 30 sounds at
                // 10:47:19.2, 20.2 and 21.5). Every frame costs nothing that shows there, and it
                // ends with the load or after 20 seconds, whichever is first.
                s_RoomsStillLoading = s_IsPlayerInsideClone && RegionRoomsStillLoading(region)
                                      && Time.unscaledTime < s_SoundSideSince + LoadingSoundCheckSeconds;
                if (s_RoomsStillLoading) s_NextRoomSoundCheckTime = 0f;
                // Off since 2026-10-12: the outdoors is turned down by the game's own filter at the
                // door instead (ApplyInsideOcclusion), not paused sound by sound. The pause found
                // each animal sound about a second after it began and cut it there (the owner: "I
                // still see sound pausing... We should NOT be doing that").
                if (s_PauseOutdoorSoundsInside) KeepOutdoorSoundsOut(); else LetOutdoorSoundsBackIn();

                // Wait for this region's rooms, and only this region's: ActiveInteriors still holds the
                // last region's rooms after travelling, and those never finish again (measured
                // 2026-10-11: four Airfield rooms listed as not ready while standing in Mystery Lake).
                bool anyRoomHere = false;
                foreach (var instance in ActiveInteriors.Values)
                {
                    if (instance == null || instance.Config == null) continue;
                    if (instance.Config.ExteriorSceneName != region) continue;
                    if (!instance.RunCompleted) return;
                    anyRoomHere = true;
                }
                if (!anyRoomHere) return;

                KeepRegionSoundStates(region);
                MakeWindLoopFollowThePlayer(s_IsPlayerInsideClone);
                FollowRoomAmbience(region);
                QuietFarCarryingSourcesWhileInside();
                QuietAnimalsWhileInside();
            }
            catch (System.Exception e)
            {
                // Sound must never take the game down with it; say so once a minute at most.
                s_NextRoomSoundCheckTime = Time.unscaledTime + 60f;
                MelonLogger.Msg($"[ROOM-SOUND] denetim basarisiz: {e.Message}",
                                $"[ROOM-SOUND] the check failed: {e.Message}");
            }
        }

        // HOW MUCH THE OUTDOORS IS TURNED DOWN IN A ROOM FOLLOWS THE WIND
        //
        // The two rules below stand in for the outdoor wind, which covers the far-carrying sources
        // and the animals outdoors and is not heard in a room. Their numbers were set by ear in a
        // blizzard. With no wind there is nothing to stand in for, and the owner's fear on hearing
        // how they worked was "that we'll make them silent even during clear weather". So both
        // scales run from 1, untouched, at s_WindCalmMph or less to their set value at
        // s_WindFullMph or more (the blizzard they were set in was 65 mph; clear and calm read 11
        // to 12). One read of the wind a second; the scales are sent again only when the share has
        // moved by more than 0.03.
        private static float s_WindCalmMph = 10f;
        private static float s_WindFullMph = 60f;

        private static float WindShareNow()
        {
            var wind = GameManager.GetWindComponent();
            if (wind == null) return 0f;
            return Mathf.Clamp01((wind.m_CurrentMPH - s_WindCalmMph) / Mathf.Max(s_WindFullMph - s_WindCalmMph, 1f));
        }

        // THE OUTDOOR SOURCES THAT CARRY FAR, HEARD FROM A ROOM
        //
        // Some fixed outdoor sources are made to be heard a long way off: the game keeps them
        // running out to 500 m where most stop at 200 m. Outdoors the wind covers them. A real
        // interior does not have them at all. A room here has neither: the outdoor wind loop is
        // swapped for the much quieter indoor one, so such a source is left alone on top of the
        // room. Measured 2026-10-12 in the Airfield hangar, wind loop off: the flag by the security
        // gate, 133 m off on the ground and 154 m from the room, loses only about 9 dB between
        // 20 m and 154 m, and the owner heard it just as loud outdoors with the wind off ("Holy
        // crap, it is that loud in the blizzard from here"). With the sound engine's distance
        // scale at 0.3 on it he could not hear it from there, and at 1 it was back.
        //
        // So while the player is in a room, every fixed source the game runs out to 500 m or more
        // fades as if it were about three times as far away, and on the way out it is put back.
        // Nearer-running sources, animals and everything that moves are not touched. One walk of
        // the tracker list at the door, and again only if the list grows while inside.
        private static float s_FarSourceScaleInside = 0.3f;
        private static float s_FarSourceRadius = 500f;
        private static bool s_FarSourcesQuietened = false;
        private static float s_FarSourceShareApplied = 0f;
        private static int s_FarSourceTrackerCount = -1;
        private static readonly System.Collections.Generic.List<GameObject> s_FarSourceObjects = new System.Collections.Generic.List<GameObject>();

        private static void QuietFarCarryingSourcesWhileInside()
        {
            bool inside = s_IsPlayerInsideClone;
            var trackers = Il2CppAudio.SimpleAudio.PlayAudioSimpleManager.m_Trackers;
            int count = trackers != null ? trackers.Count : 0;
            float share = inside ? WindShareNow() : 0f;
            float scaleNow = Mathf.Lerp(1f, s_FarSourceScaleInside, share);
            if (inside == s_FarSourcesQuietened && (!inside || count == s_FarSourceTrackerCount))
            {
                // Same side of the door and the same sources: only the wind can have moved.
                if (!inside || Mathf.Abs(share - s_FarSourceShareApplied) <= 0.03f) return;
                s_FarSourceShareApplied = share;
                for (int i = 0; i < s_FarSourceObjects.Count; i++)
                    if (s_FarSourceObjects[i] != null) AkSoundEngine.SetScalingFactor(s_FarSourceObjects[i], scaleNow);
                return;
            }

            // Back to normal first, always; that also covers a scale changed while inside.
            int putBack = 0;
            for (int i = 0; i < s_FarSourceObjects.Count; i++)
            {
                if (s_FarSourceObjects[i] == null) continue;
                AkSoundEngine.SetScalingFactor(s_FarSourceObjects[i], 1f);
                putBack++;
            }
            s_FarSourceObjects.Clear();
            s_FarSourcesQuietened = inside;
            s_FarSourceTrackerCount = count;
            s_FarSourceShareApplied = share;

            if (inside && trackers != null)
            {
                for (int i = 0; i < count; i++)
                {
                    var tracker = trackers[i];
                    if (tracker == null || tracker.m_Radius < s_FarSourceRadius) continue;
                    GameObject go = tracker.m_GameObject;
                    if (go == null) continue;

                    Transform root = go.transform.root;
                    if (s_RoomSoundRoots.Contains(root.GetInstanceID())) continue;
                    if (root.name.StartsWith("SCRIPT_") || root.name.StartsWith("CHARACTER_") || root.name.StartsWith("WILDLIFE_")) continue;

                    // Only objects that already carry a sound object: asking the engine about any
                    // other would leave a new AkGameObj behind on it.
                    var emitters = go.GetComponentsInChildren<AkGameObj>(true);
                    for (int e = 0; e < emitters.Length; e++)
                    {
                        if (emitters[e] == null) continue;
                        GameObject emitter = emitters[e].gameObject;
                        AkSoundEngine.SetScalingFactor(emitter, scaleNow);
                        s_FarSourceObjects.Add(emitter);
                    }
                }
            }

            if (putBack > 0 || s_FarSourceObjects.Count > 0)
                MelonLogger.Msg($"[ROOM-SOUND] Uzaga tasinan dis ses kaynaklari: {putBack} tanesi normale donduruldu, {s_FarSourceObjects.Count} tanesi {scaleNow:F2} olcegiyle kisildi (ruzgar payi {share:F2}, tam deger {s_FarSourceScaleInside:F2}).",
                                $"[ROOM-SOUND] Far-carrying outdoor sources: {putBack} put back to normal, {s_FarSourceObjects.Count} given distance scale {scaleNow:F2} (wind share {share:F2} of the full {s_FarSourceScaleInside:F2}; 1.00 means untouched).");
        }

        // SOUND WHEN THIS MOD'S BLACK LOADING SCREEN LIFTS
        //
        // After a load the screen is held black while the rooms are built, then the picture comes
        // back over half a second. The sound came in all at once: "It's very abrupt and shocking
        // in a blizzard" (the owner, 2026-10-12, who asked for one to two seconds). So while the
        // screen is held the game's global volume parameter is kept at zero, every frame, because
        // the game sets it too; and when the screen is let go it is run back up to the level it
        // had before, over s_SoundAfterBlackScreenMs, by the sound engine's own interpolation. Tried by hand first in
        // the running game: zero, then a 1500 ms run to 100, both accepted.
        //
        // Every way out of the held screen clears s_ScreenHeldBlack, the 60 second safety
        // included, so the sound cannot be left off.
        // Two seconds by the owner's word on the first build that had this at 1500 ("much better but
        // make it 2s in the mod", build 8c9429c).
        private static int s_SoundAfterBlackScreenMs = 2000;
        private static bool s_SoundHeldForBlackScreen = false;
        private static bool s_SoundHoldFailed = false;
        private static float s_SoundLevelAfterBlackScreen = 100f;
        private static float s_SoundLevelFoundBeforeBlackScreen = float.NaN;
        private static float s_SoundLetGoAt = -1000f;

        private static void BringSoundInSlowlyAfterTheBlackScreen()
        {
            if (s_SoundHoldFailed) return;
            try
            {
                if (s_ScreenHeldBlack)
                {
                    if (!s_SoundHeldForBlackScreen)
                    {
                        // The level to go back to is the one found, never a constant: this may be
                        // where the game keeps a player's own volume setting. A reading of nothing
                        // or of zero cannot be told from a level the game had already turned down
                        // for its own load, so that one case goes back to full, and says so below.
                        //
                        // A second load straight after a first would find the level still on its
                        // way up from the first one; then the first one's level is kept.
                        bool stillComingBack = Time.unscaledTime < s_SoundLetGoAt + s_SoundAfterBlackScreenMs / 1000f + 0.5f;
                        if (!stillComingBack)
                        {
                            float found = float.NaN;
                            int kind = 1;
                            AkSoundEngine.GetRTPCValue(Il2CppAK.GAME_PARAMETERS.GLOBALVOLUME, ulong.MaxValue, 0u, out found, ref kind);
                            s_SoundLevelFoundBeforeBlackScreen = found;
                            s_SoundLevelAfterBlackScreen = float.IsNaN(found) || found < 1f ? 100f : found;
                        }
                        s_SoundHeldForBlackScreen = true;
                    }
                    AkSoundEngine.SetRTPCValue("GLOBALVOLUME", 0f);
                }
                else if (s_SoundHeldForBlackScreen)
                {
                    s_SoundHeldForBlackScreen = false;
                    s_SoundLetGoAt = Time.unscaledTime;
                    AkSoundEngine.SetRTPCValue("GLOBALVOLUME", s_SoundLevelAfterBlackScreen, null, Mathf.Max(0, s_SoundAfterBlackScreenMs));
                    MelonLogger.Msg($"[ROOM-SOUND] Siyah yukleme ekrani kalkti; ses {s_SoundAfterBlackScreenMs} ms icinde {s_SoundLevelAfterBlackScreen:F0} seviyesine geri geliyor (ekran kararmadan once okunan: {s_SoundLevelFoundBeforeBlackScreen:F0}).",
                                    $"[ROOM-SOUND] The black loading screen lifted; sound comes back to level {s_SoundLevelAfterBlackScreen:F0} over {s_SoundAfterBlackScreenMs} ms (read before the screen went black: {s_SoundLevelFoundBeforeBlackScreen:F0}).");
                }
            }
            catch (System.Exception e)
            {
                // Runs every frame: one failure ends it for the session, with the volume put back.
                s_SoundHoldFailed = true;
                s_SoundHeldForBlackScreen = false;
                try { AkSoundEngine.SetRTPCValue("GLOBALVOLUME", s_SoundLevelAfterBlackScreen); } catch { }
                MelonLogger.Msg($"[ROOM-SOUND] yukleme ekrani ses gecisi basarisiz, bu oturumda bir daha denenmeyecek: {e.Message}",
                                $"[ROOM-SOUND] the sound fade after the loading screen failed and will not be tried again this session: {e.Message}");
            }
        }

        // ANIMALS HEARD FROM A ROOM
        //
        // The same case as the far-carrying fixed sources above. An animal's footsteps are made to
        // carry 300 m (500 for a moose or a cougar), outdoors the wind covers them, and a room has
        // only the quiet indoor wind loop. On build 8c9429c, in the Airfield hangar in a blizzard
        // with the fixed sources already turned down, the owner: "the animals are too loud. Why is
        // their hoof noise so much louder than it should be compared to the clattering?"
        //
        // So while the player is in a room every sound object on an animal gets a distance scale
        // too, milder than the fixed sources' because animals near a window are meant to be heard.
        // Animals come and go, so the game's own list of them is walked once a second while
        // inside; an object is set once and all are put back on the way out.
        private static float s_AnimalScaleInside = 0.5f;
        private static bool s_AnimalsQuietened = false;
        private static float s_AnimalShareApplied = 0f;
        private static readonly System.Collections.Generic.List<GameObject> s_AnimalSoundObjects = new System.Collections.Generic.List<GameObject>();
        private static readonly System.Collections.Generic.HashSet<int> s_AnimalSoundObjectIds = new System.Collections.Generic.HashSet<int>();

        private static void PutAnimalSoundsBack()
        {
            for (int i = 0; i < s_AnimalSoundObjects.Count; i++)
                if (s_AnimalSoundObjects[i] != null) AkSoundEngine.SetScalingFactor(s_AnimalSoundObjects[i], 1f);
            s_AnimalSoundObjects.Clear();
            s_AnimalSoundObjectIds.Clear();
        }

        private static void QuietAnimalsWhileInside()
        {
            bool inside = s_IsPlayerInsideClone && s_AnimalScaleInside < 0.999f;
            if (!inside)
            {
                if (!s_AnimalsQuietened) return;
                int back = s_AnimalSoundObjects.Count;
                PutAnimalSoundsBack();
                s_AnimalsQuietened = false;
                MelonLogger.Msg($"[ROOM-SOUND] Hayvan sesleri: {back} ses nesnesi normale donduruldu.",
                                $"[ROOM-SOUND] Animal sounds: {back} sound objects put back to normal.");
                return;
            }

            var animals = BaseAiManager.m_BaseAis;
            if (animals == null) return;
            int before = s_AnimalSoundObjects.Count;

            float share = WindShareNow();
            float scaleNow = Mathf.Lerp(1f, s_AnimalScaleInside, share);
            if (!s_AnimalsQuietened || Mathf.Abs(share - s_AnimalShareApplied) > 0.03f)
            {
                s_AnimalShareApplied = share;
                for (int i = 0; i < s_AnimalSoundObjects.Count; i++)
                    if (s_AnimalSoundObjects[i] != null) AkSoundEngine.SetScalingFactor(s_AnimalSoundObjects[i], scaleNow);
            }
            else scaleNow = Mathf.Lerp(1f, s_AnimalScaleInside, s_AnimalShareApplied);
            for (int i = 0; i < animals.Count; i++)
            {
                var animal = animals[i];
                if (animal == null) continue;
                var emitters = animal.gameObject.GetComponentsInChildren<AkGameObj>(true);
                for (int e = 0; e < emitters.Length; e++)
                {
                    if (emitters[e] == null) continue;
                    GameObject emitter = emitters[e].gameObject;
                    if (!s_AnimalSoundObjectIds.Add(emitter.GetInstanceID())) continue;
                    AkSoundEngine.SetScalingFactor(emitter, scaleNow);
                    s_AnimalSoundObjects.Add(emitter);
                }
            }

            if (!s_AnimalsQuietened)
            {
                s_AnimalsQuietened = true;
                MelonLogger.Msg($"[ROOM-SOUND] Hayvan sesleri: {s_AnimalSoundObjects.Count - before} ses nesnesi {scaleNow:F2} olcegiyle kisildi (ruzgar payi {s_AnimalShareApplied:F2}, tam deger {s_AnimalScaleInside:F2}); iceride kalindikca yeni gelenler de kisilir.",
                                $"[ROOM-SOUND] Animal sounds: {s_AnimalSoundObjects.Count - before} sound objects given distance scale {scaleNow:F2} (wind share {s_AnimalShareApplied:F2} of the full {s_AnimalScaleInside:F2}; 1.00 means untouched); newcomers follow while inside.");
            }
        }

        private static bool RegionRoomsStillLoading(string region)
        {
            foreach (var instance in ActiveInteriors.Values)
            {
                if (instance == null || instance.Config == null) continue;
                if (instance.Config.ExteriorSceneName == region && !instance.RunCompleted) return true;
            }
            return false;
        }

        private static void KeepRegionSoundStates(string region)
        {
            if (s_SoundStateRegion != region)
            {
                s_SoundStateRegion = region;
                s_RegionSceneState = 0;
                s_RegionSceneStateSearches = 0;
                s_SoundStateCorrections = 0;
            }

            // The search walks every AkState in the scene, so it is tried a few times and then left.
            if (s_RegionSceneState == 0)
            {
                if (s_RegionSceneStateSearches >= 3) return;
                s_RegionSceneStateSearches++;
                s_RegionSceneState = FindRegionSceneState(region);
                if (s_RegionSceneState == 0) return;
            }

            uint scene = 0, interior = 0;
            AkSoundEngine.GetState(Il2CppAK.STATES.SCENE.GROUP, out scene);
            AkSoundEngine.GetState(Il2CppAK.STATES.INTERIORSTATES.GROUP, out interior);

            bool sceneWrong = scene != s_RegionSceneState;
            bool interiorWrong = interior == Il2CppAK.STATES.INTERIORSTATES.STATE.INTERIORSTATE_INT;
            if (!sceneWrong && !interiorWrong) return;

            if (sceneWrong) AkSoundEngine.SetState(Il2CppAK.STATES.SCENE.GROUP, s_RegionSceneState);
            if (interiorWrong) AkSoundEngine.SetState(Il2CppAK.STATES.INTERIORSTATES.GROUP, Il2CppAK.STATES.INTERIORSTATES.STATE.NONE);

            s_SoundStateCorrections++;
            if (s_SoundStateCorrections <= 5 || s_DebugBounds)
                MelonLogger.Msg($"[ROOM-SOUND] {region}: ses motoru bir ic mekani gosteriyordu (Scene {scene}, InteriorStates {interior}); bolgenin kendi degerleri geri verildi (Scene {s_RegionSceneState}). Duzeltme {s_SoundStateCorrections}.",
                                $"[ROOM-SOUND] {region}: the sound engine was set for an interior (Scene {scene}, InteriorStates {interior}); the region's own values were put back (Scene {s_RegionSceneState}). Correction {s_SoundStateCorrections}.");
        }

        // The Scene value the region's own AkState sets when it starts: not None, fired on Start,
        // and not part of any room this mod built. None when the region has no such component, and
        // zero when there is more than one value to choose from or the search is still being retried.
        private static uint FindRegionSceneState(string region)
        {
            uint found = 0;
            int distinct = 0;

            var states = UnityEngine.Object.FindObjectsOfType<AkState>(true);
            for (int i = 0; i < states.Length; i++)
            {
                var state = states[i];
                if (state == null || state.data == null) continue;
                if (state.data.GroupId != Il2CppAK.STATES.SCENE.GROUP) continue;

                uint value = state.data.Id;
                if (value == 0 || value == Il2CppAK.STATES.SCENE.STATE.NONE) continue;

                bool onStart = false;
                var triggers = state.triggerList;
                if (triggers != null)
                    for (int t = 0; t < triggers.Count; t++)
                        if (triggers[t] == SoundTriggerOnStart) { onStart = true; break; }
                if (!onStart) continue;

                bool inRoom = false;
                foreach (var instance in ActiveInteriors.Values)
                {
                    if (instance == null || instance.MasterInterior == null) continue;
                    if (state.transform.IsChildOf(instance.MasterInterior.transform)) { inRoom = true; break; }
                }
                if (inRoom) continue;

                // The region's own scenes are the region and its add-ons (AirfieldRegion_SANDBOX, ...).
                string owner = RealSceneName(state.gameObject.scene);
                if (owner == null || !owner.StartsWith(region, System.StringComparison.Ordinal)) continue;

                if (value != found) { found = value; distinct++; }
            }

            if (distinct == 1)
            {
                MelonLogger.Msg($"[ROOM-SOUND] {region}: bolgenin kendi Scene ses durumu bulundu ({found}).",
                                $"[ROOM-SOUND] {region}: found the region's own Scene sound state ({found}).");
                return found;
            }

            // A region with no Scene component of its own never sets the state, so its value is None.
            // Measured 2026-10-11 in Mystery Lake: no AkState for Scene or InteriorStates anywhere in
            // the region, and both states read None outdoors.
            if (distinct == 0 && s_RegionSceneStateSearches >= 3)
            {
                MelonLogger.Msg($"[ROOM-SOUND] {region}: bolgenin kendi Scene ses durumu yok; deger None olarak tutulacak.",
                                $"[ROOM-SOUND] {region}: the region has no Scene sound state of its own; None is kept in force.");
                return Il2CppAK.STATES.SCENE.STATE.NONE;
            }

            if (distinct > 1 && s_RegionSceneStateSearches >= 3)
                MelonLogger.Msg($"[ROOM-SOUND] {region}: bolgenin kendi Scene ses durumu secilemedi ({distinct} aday); ses durumlarina dokunulmuyor.",
                                $"[ROOM-SOUND] {region}: could not pick out the region's own Scene sound state ({distinct} candidates); the sound states are left alone.");
            return 0;
        }

        // ─── THE WIND LOOP FOLLOWS THE PLAYER THROUGH THE DOOR ───
        //
        // The game has two wind loops, Play_WindOutdoors and Play_WindIndoors, and one slot for the
        // one that is playing (Wind.m_WindLoopAudioInstance). A real interior plays the indoor one
        // with no occlusion filter: measured 2026-10-11 in the real Airfield hangar and the real
        // Trapper's Cabin. A parked room stands in the outdoor scene, so left alone it keeps the
        // outdoor loop.
        //
        // The game cannot be asked for the indoor loop here: Wind.PlayProceduralWindAudio picks the
        // event itself and gave the outdoor one inside a parked room three times running
        // (2026-10-11). So the indoor event is posted directly on the object the game posts its own
        // wind on and handed to the game as its loop. The owner, in the parked Trapper's Cabin at
        // 65 mph, 2026-10-12: "Now this sounds right!"
        //
        // The outdoor loop is PAUSED while the player is inside, not stopped, and resumed on the way
        // out. A wind loop that is started afresh plays without its main layer for a while
        // (measured 2026-09-22 and again 2026-10-11), and a pause keeps the loop as it was: paused
        // and resumed twice on 2026-10-12, the owner heard it come back as before both times.
        //
        // Called once a second from TickRoomSound with the inside flag, and at once from
        // SetAudioOcclusion with the side it was called for: the door calls SetAudioOcclusion one
        // line BEFORE it sets the inside flag, so reading the flag there left the way in to the
        // once-a-second check (the owner, 2026-10-12: "a little late"). It acts only when the loop
        // in the slot is the wrong one for that side of the door.
        public static void MakeWindLoopFollowThePlayer(bool inside)
        {
            if (inside != s_WindLoopSideInside) { s_WindLoopSideInside = inside; s_WindLoopFailures = 0; }
            if (s_WindLoopFailures >= 3) return;

            var wind = GameManager.GetWindComponent();
            if (wind == null) return;

            uint id = wind.m_WindLoopAudioInstance;
            uint playing = id == 0 ? 0 : AkSoundEngine.GetEventIDFromPlayingID(id);

            if (inside)
            {
                if (playing == IndoorWindEvent) return;

                GameObject emitter = FindWindEmitter();
                uint indoor = emitter == null ? 0 : GameAudioManager.PlaySound("Play_WindIndoors", emitter);
                if (indoor == 0)
                {
                    s_WindLoopFailures++;
                    MelonLogger.Msg($"[ROOM-SOUND] Ic mekan ruzgar dongusu baslatilamadi ({(emitter == null ? "ses nesnesi bulunamadi" : "olay calmadi")}). Deneme {s_WindLoopFailures}.",
                                    $"[ROOM-SOUND] The indoor wind loop could not be started ({(emitter == null ? "the sound object was not found" : "the event did not play")}). Try {s_WindLoopFailures}.");
                    return;
                }

                if (playing == OutdoorWindEvent)
                {
                    if (s_PausedOutdoorWindId != 0 && s_PausedOutdoorWindId != id)
                        AkSoundEngine.StopPlayingID(s_PausedOutdoorWindId, 0, AkCurveInterpolation.AkCurveInterpolation_Linear);
                    AkSoundEngine.ExecuteActionOnPlayingID(AkActionOnEventType.AkActionOnEventType_Pause, id, s_OutdoorsFadeOutMs, AkCurveInterpolation.AkCurveInterpolation_Linear);
                    s_PausedOutdoorWindId = id;
                    s_OutdoorWindPauses = 1;
                }
                else if (playing != 0)
                {
                    AkSoundEngine.StopPlayingID(id, s_OutdoorsFadeOutMs, AkCurveInterpolation.AkCurveInterpolation_Linear);
                }

                wind.m_WindLoopAudioInstance = indoor;
                wind.m_WindAudioForceStopped = false;
                MelonLogger.Msg($"[ROOM-SOUND] Oyuncu iceride: ic mekan ruzgar dongusu baslatildi (id {indoor}), dis mekan dongusu {(s_PausedOutdoorWindId == id && id != 0 ? "duraklatildi" : "yoktu")} (id {id}).",
                                $"[ROOM-SOUND] The player is inside: the indoor wind loop was started (id {indoor}); the outdoor loop {(s_PausedOutdoorWindId == id && id != 0 ? "was paused" : "was not there")} (id {id}).");
                return;
            }

            uint paused = s_PausedOutdoorWindId;
            bool pausedAlive = paused != 0 && AkSoundEngine.GetEventIDFromPlayingID(paused) == OutdoorWindEvent;

            if (playing == IndoorWindEvent)
            {
                s_PausedOutdoorWindId = 0;
                if (pausedAlive)
                {
                    AkSoundEngine.StopPlayingID(id, s_IndoorWindFadeOutMs, AkCurveInterpolation.AkCurveInterpolation_Linear);
                    ResumeSound(paused, s_OutdoorWindPauses);
                    wind.m_WindLoopAudioInstance = paused;
                    wind.m_WindAudioForceStopped = false;
                    MelonLogger.Msg($"[ROOM-SOUND] Oyuncu disarida: ic mekan ruzgar dongusu durduruldu (id {id}), dis mekan dongusu kaldigi yerden devam ediyor (id {paused}).",
                                    $"[ROOM-SOUND] The player is outside: the indoor wind loop was stopped (id {id}) and the outdoor loop carries on from where it was paused (id {paused}).");
                }
                else
                {
                    // No paused outdoor loop to go back to, for example a save loaded inside a room.
                    // Starting one afresh is the only way left; it acts at most three times a side.
                    s_WindLoopFailures++;
                    ForceRestartWindAudio(wind, "outdoor-loop-after-leaving");
                }
                return;
            }

            if (paused == 0) return;

            // Outside with something other than the indoor loop in the slot, and a paused outdoor
            // loop still remembered: the game started a loop of its own meanwhile, or stopped ours.
            s_PausedOutdoorWindId = 0;
            if (!pausedAlive) return;
            if (playing == OutdoorWindEvent && id != paused)
            {
                AkSoundEngine.StopPlayingID(paused, 0, AkCurveInterpolation.AkCurveInterpolation_Linear);
            }
            else if (id != paused)
            {
                ResumeSound(paused, s_OutdoorWindPauses);
                wind.m_WindLoopAudioInstance = paused;
                wind.m_WindAudioForceStopped = false;
            }
        }

        // The object the game posts its own wind loop on: CHARACTER_FPSPlayer/WorldView/WorldView_SoundEmitter.
        private static GameObject FindWindEmitter()
        {
            GameObject player = GameManager.GetPlayerObject();
            if (player == null) return null;
            Transform t = player.transform.Find("WorldView_SoundEmitter");
            if (t == null) t = player.transform.Find("WorldView/WorldView_SoundEmitter");
            return t == null ? null : t.gameObject;
        }

        // ─── THE ROOM'S OWN SOUNDS ───
        //
        // A room's creaks, window rattles, drips and entrance wind are PlayAudioSimple and
        // PlayAmbientSimple components. Neither posts a sound. Each registers a tracker in
        // PlayAudioSimpleManager.m_Trackers, and the manager starts and stops the sound by the
        // listener's distance to the tracker's own copy of the position, m_Pos, taken when the
        // component registered. Whether the object is active plays no part.
        //
        // A parked room is built at one place and then moved, so its trackers remember where the
        // interior scene stood. Measured 2026-10-12 in the Airfield: every tracker of the two rooms
        // not yet visited was 374 to 1441 m from its object and none was playing. That is why a
        // parked room was silent. Up to then the mod ran Start again on the emitters at the door,
        // which registers one more tracker per visit and removes none (546 trackers on the
        // hangar's 152 objects after a few visits), and then stopped the extra copies it could
        // find. Both are gone; the cause is the position, so the position is what is corrected.
        //
        // Measured the same day on a room that was switched off, one LoopTracker and one
        // AmbientTracker, each both ways: m_Pos written to where the player stood turned the
        // tracker active within one round of the manager (about 2 s; it handles 10 trackers a
        // frame), m_Pos written back turned it off, and MaybeStartOrStop called right after the
        // write did either at once. So a room that is switched off still plays when its trackers
        // are in reach, and a room parked 80 m overhead is in reach of most of its own emitters
        // (200 to 300 m). Hence the rule: the occupied room's trackers stand at their objects,
        // and every other room's stand nowhere, as a loading-screen interior does not exist
        // while the player is outdoors.
        //
        // The whole list is gone through again when the occupied room changes, when the region
        // changes, and when the list's length changes (a component that starts late registers
        // late). Nothing per frame, and nothing while those three stay the same.

        private static SeamlessInteriorInstance s_RoomSoundInstance = null;
        private static string s_RoomSoundRegion = null;
        private static int s_RoomSoundTrackerCount = -1;
        private static readonly System.Collections.Generic.HashSet<int> s_RoomSoundRoots = new System.Collections.Generic.HashSet<int>();
        private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<uint> s_PlayingIdBuffer = null;

        // Far from anywhere a player can stand, in every region.
        private static readonly Vector3 s_NowhereForRoomSounds = new Vector3(0f, -100000f, 0f);

        private static void FollowRoomAmbience(string region)
        {
            var trackers = Il2CppAudio.SimpleAudio.PlayAudioSimpleManager.m_Trackers;
            if (trackers == null) return;

            SeamlessInteriorInstance wanted = s_IsPlayerInsideClone ? ResolveOccupiedInstance() : null;
            int count = trackers.Count;
            if (ReferenceEquals(wanted, s_RoomSoundInstance) && count == s_RoomSoundTrackerCount && region == s_RoomSoundRegion) return;

            s_RoomSoundInstance = wanted;
            s_RoomSoundTrackerCount = count;
            s_RoomSoundRegion = region;

            // A room is found by its root. One that hangs under something else is left as it was.
            s_RoomSoundRoots.Clear();
            foreach (var instance in ActiveInteriors.Values)
            {
                if (instance == null || instance.MasterInterior == null) continue;
                Transform root = instance.MasterInterior.transform;
                if (root.parent == null) s_RoomSoundRoots.Add(root.GetInstanceID());
            }

            int occupiedRoot = wanted != null && wanted.MasterInterior != null ? wanted.MasterInterior.transform.GetInstanceID() : 0;
            Transform player = GameManager.GetPlayerTransform();
            if (player == null) return;
            Vector3 listener = player.position;

            float began = Time.realtimeSinceStartup;
            int broughtIn = 0, sentAway = 0;

            for (int i = 0; i < count; i++)
            {
                var tracker = trackers[i];
                if (tracker == null) continue;
                GameObject go = tracker.m_GameObject;
                if (go == null) continue;

                int root = go.transform.root.GetInstanceID();
                if (!s_RoomSoundRoots.Contains(root)) continue;

                bool occupied = root == occupiedRoot;
                Vector3 place = occupied ? go.transform.position : s_NowhereForRoomSounds;
                if ((tracker.m_Pos - place).sqrMagnitude < 0.0001f) continue;

                tracker.m_Pos = place;
                // The manager would get to it within its round; at a door that is too late on the
                // way out, where the room's sounds would follow the player outdoors for two seconds.
                tracker.MaybeStartOrStop(listener);
                if (occupied) broughtIn++; else sentAway++;
            }

            if (broughtIn > 0 || sentAway > 0)
            {
                float ms = (Time.realtimeSinceStartup - began) * 1000f;
                string room = wanted != null ? wanted.Config.ResolvedInstanceId : "-";
                MelonLogger.Msg($"[ROOM-SOUND] oda sesleri: icerideki oda {room}; {broughtIn} ses kaynagi yerine getirildi, {sentAway} tanesi uzaga gonderildi ({count} kayit, {ms:F1} ms).",
                                $"[ROOM-SOUND] room sounds: occupied room {room}; {broughtIn} sound trackers put at their objects, {sentAway} sent away ({count} trackers looked at, {ms:F1} ms).");
            }
        }

        // ─── THE OUTDOORS IS NOT HEARD IN A ROOM ───
        //
        // A loading-screen interior has no outdoors: the region scene is not loaded, so none of its
        // sound sources exist. A parked room stands in the region scene, and every one of them
        // still plays with its full reach. Found 2026-10-12 in the parked Airfield hangar at 65 mph:
        // the owner heard "a weird cloth rustling", "like a flag really", which was not there in
        // the real hangar with this mod disabled. It was the region's one Play_FlagFlapping emitter,
        // 152 m away with a reach of 500 m; pausing that event made it go ("Gone"). The region had
        // 68 kinds of such source in reach of the room (flood lights, cars, tanks, tree creaks,
        // outdoor window rattles, wind-driven props). Up to 2.2.0 the Heavy occlusion filter
        // muffled all of it along with the room itself.
        //
        // So while the player is inside, every sound playing on an object of the loaded scenes that
        // is not part of the occupied room is PAUSED, and resumed on the way out. Paused, not
        // stopped: the game starts these once and does not start them again, and a pause keeps a
        // loop as it was (see the wind loop above).
        //
        // What counts as the room: anything under its root, and anything standing inside its
        // volume (a lantern the player put down in there is not parented to the room). The player,
        // the game's own system objects and everything that survives scene loads are left alone.
        //
        // The sound engine has no list of what is playing, only an answer per object, and only
        // objects with an AkGameObj have ever played anything. Resources.FindObjectsOfTypeAll is
        // used because Object.FindObjectsOfType returned 105 of the 321 here and none of the
        // region's (measured the same day; why is not known). Cost: once a second while inside, one
        // query per such object; nothing per frame, and nothing at all while outside.
        private static readonly System.Collections.Generic.List<uint> s_PausedOutdoorSounds = new System.Collections.Generic.List<uint>();
        private static readonly System.Collections.Generic.HashSet<uint> s_PausedOutdoorSoundSet = new System.Collections.Generic.HashSet<uint>();

        private static void KeepOutdoorSoundsOut()
        {
            if (!s_IsPlayerInsideClone) { LetOutdoorSoundsBackIn(); return; }

            // While the room is still being built there is no room root to spare yet. Nothing of the
            // room is playing then either: its sounds are started only once it stands.
            SeamlessInteriorInstance room = ResolveOccupiedInstance();
            Transform roomRoot = room != null && room.MasterInterior != null ? room.MasterInterior.transform : null;

            // No answer while some room does stand open means the player is between two of them;
            // pausing now could catch a room's own sounds, which nothing would resume until the
            // player left. Wait a check for it to settle.
            if (roomRoot == null)
            {
                foreach (var other in ActiveInteriors.Values)
                    if (other != null && other.MasterInterior != null && other.MasterInterior.activeInHierarchy) return;
            }
            GameObject player = GameManager.GetPlayerObject();
            Transform playerRoot = player != null ? player.transform.root : null;

            var known = Resources.FindObjectsOfTypeAll<AkGameObj>();
            int pausedNow = 0;

            for (int i = 0; i < known.Length; i++)
            {
                var ak = known[i];
                if (ak == null) continue;

                GameObject go = ak.gameObject;
                if (!go.activeInHierarchy) continue;

                var scene = go.scene;
                if (!scene.IsValid() || scene.name == "DontDestroyOnLoad") continue;

                Transform t = go.transform;
                Transform root = t.root;
                if (root == playerRoot || root.name.StartsWith("SCRIPT_") || root.name.StartsWith("CHARACTER_")) continue;
                if (roomRoot != null && (t.IsChildOf(roomRoot) || room.IsPositionInVolume(t.position, 1f))) continue;

                int playing = PlayingOn(go);
                for (int k = 0; k < playing; k++)
                {
                    uint id = s_PlayingIdBuffer[k];
                    if (!s_PausedOutdoorSoundSet.Add(id)) continue;

                    AkSoundEngine.ExecuteActionOnPlayingID(AkActionOnEventType.AkActionOnEventType_Pause, id, s_RoomsStillLoading ? 0 : s_OutdoorsFadeOutMs, AkCurveInterpolation.AkCurveInterpolation_Linear);
                    s_PausedOutdoorSounds.Add(id);
                    pausedNow++;
                    if (s_OutdoorSoundPauses == 0) s_OutdoorSoundPauses = 1;
                }
            }

            if (pausedNow > 0)
                MelonLogger.Msg($"[ROOM-SOUND] {(room != null ? room.Config.ResolvedInstanceId : "oda henuz yok")}: disaridaki {pausedNow} ses duraklatildi (toplam {s_PausedOutdoorSounds.Count}).",
                                $"[ROOM-SOUND] {(room != null ? room.Config.ResolvedInstanceId : "room not built yet")}: paused {pausedNow} outdoor sounds ({s_PausedOutdoorSounds.Count} in all).");
        }

        private static void LetOutdoorSoundsBackIn()
        {
            if (s_PausedOutdoorSounds.Count == 0) return;

            for (int i = 0; i < s_PausedOutdoorSounds.Count; i++)
                ResumeSound(s_PausedOutdoorSounds[i], s_OutdoorSoundPauses);

            MelonLogger.Msg($"[ROOM-SOUND] Oyuncu disarida: duraklatilan {s_PausedOutdoorSounds.Count} dis ses devam ediyor.",
                            $"[ROOM-SOUND] The player is outside: the {s_PausedOutdoorSounds.Count} paused outdoor sounds carry on.");

            s_PausedOutdoorSounds.Clear();
            s_PausedOutdoorSoundSet.Clear();
            s_OutdoorSoundPauses = 0;
        }

        // ─── THE GAME'S OWN PAUSE UNDOES OURS ───
        //
        // The game pauses and resumes all sound around the pause menu through its master audio bus
        // (the events Pause_Master_Audio_Bus and Resume_Master_Audio_Bus), and that resume also
        // un-pauses what this file paused on purpose. The owner, 2026-10-12, inside the parked
        // hangar: "going to the pause menu then back to the game switches the wind loop back to
        // outdoor". Nothing was logged at that moment, because the bookkeeping here still said
        // paused.
        //
        // So the frame the game's audio comes back, the pauses are issued again, and twice more
        // shortly after. This is one static read per frame; it does work on three frames.
        //
        // Whether the sound engine counts pauses is not known here, so every sound is resumed as
        // many times as it may have been paused. A resume too many does nothing.
        private static bool s_GameAudioWasPaused = false;
        private static bool s_GameAudioWatchFailed = false;
        private static int s_OutdoorWindPauses = 0;
        private static int s_OutdoorSoundPauses = 0;
        private static float s_PauseAgainAt1 = 0f;
        private static float s_PauseAgainAt2 = 0f;

        private static void ResumeSound(uint id, int times)
        {
            for (int i = 0; i < System.Math.Max(times, 1); i++)
                AkSoundEngine.ExecuteActionOnPlayingID(AkActionOnEventType.AkActionOnEventType_Resume, id, s_OutdoorsFadeInMs, AkCurveInterpolation.AkCurveInterpolation_Linear);
        }

        private static void PutPausesBackAfterTheGameResumesAudio()
        {
            if (s_GameAudioWatchFailed) return;
            try
            {
                bool paused = GameAudioManager.m_AudioPaused;
                bool resumedNow = s_GameAudioWasPaused && !paused;
                s_GameAudioWasPaused = paused;

                // The pause given on the frame the audio comes back does not always hold: the game
                // brings its whole mix back with one resume of the master bus, and that can land
                // after this pause and sweep it away. Measured 2026-10-12 in the Airfield hangar on
                // build 4327a55: the log had "1 sounds were paused again" at 20:20:17, the owner
                // still heard the outdoor loop ("It's the outdoor valley loop"), and the same pause
                // given by hand minutes later stopped it ("That fixed it"). So it is given twice
                // more, a quarter of a second and a second later.
                float now = Time.unscaledTime;
                if (resumedNow)
                {
                    s_PauseAgainAt1 = now + 0.25f;
                    s_PauseAgainAt2 = now + 1f;
                }
                else if (paused)
                {
                    s_PauseAgainAt1 = 0f;
                    s_PauseAgainAt2 = 0f;
                    return;
                }
                else if (s_PauseAgainAt1 > 0f && now >= s_PauseAgainAt1) s_PauseAgainAt1 = 0f;
                else if (s_PauseAgainAt2 > 0f && now >= s_PauseAgainAt2) s_PauseAgainAt2 = 0f;
                else return;

                int again = 0;
                uint wind = s_PausedOutdoorWindId;
                if (wind != 0 && AkSoundEngine.GetEventIDFromPlayingID(wind) == OutdoorWindEvent)
                {
                    AkSoundEngine.ExecuteActionOnPlayingID(AkActionOnEventType.AkActionOnEventType_Pause, wind, 0, AkCurveInterpolation.AkCurveInterpolation_Linear);
                    s_OutdoorWindPauses++;
                    again++;
                }

                for (int i = 0; i < s_PausedOutdoorSounds.Count; i++)
                {
                    AkSoundEngine.ExecuteActionOnPlayingID(AkActionOnEventType.AkActionOnEventType_Pause, s_PausedOutdoorSounds[i], 0, AkCurveInterpolation.AkCurveInterpolation_Linear);
                    again++;
                }
                if (s_PausedOutdoorSounds.Count > 0) s_OutdoorSoundPauses++;

                if (again > 0 && resumedNow)
                    MelonLogger.Msg($"[ROOM-SOUND] Oyun sesi devam ettirdi; {again} ses yeniden duraklatildi, 0.25 ve 1 saniye sonra bir daha duraklatilacak.",
                                    $"[ROOM-SOUND] The game resumed its audio; {again} sounds were paused again, and will be paused twice more, 0.25 and 1 second from now.");
            }
            catch (System.Exception e)
            {
                // Runs every frame, so one failure ends it for the session rather than filling the log.
                s_GameAudioWatchFailed = true;
                MelonLogger.Msg($"[ROOM-SOUND] yeniden duraklatma basarisiz, bu oturumda bir daha denenmeyecek: {e.Message}",
                                $"[ROOM-SOUND] pausing again failed and will not be tried again this session: {e.Message}");
            }
        }

        // How many sounds the engine has playing on the object; the ids are left in the buffer.
        private static int PlayingOn(GameObject go)
        {
            if (s_PlayingIdBuffer == null)
                s_PlayingIdBuffer = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<uint>(32);

            // Asking the sound engine about an object registers the object with it and leaves an
            // AkGameObj component behind (measured 2026-10-11: a console scan of 556 room objects left
            // 556 of them). An object without one has never played anything, so do not ask.
            if (go.GetComponent<AkGameObj>() == null) return 0;

            uint count = 32;
            AkSoundEngine.GetPlayingIDsFromGameObject(go, ref count, s_PlayingIdBuffer);
            return (int)System.Math.Min(count, 32u);
        }
    }
}
