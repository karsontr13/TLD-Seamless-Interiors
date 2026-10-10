using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // Static definition of every interior the mod clones, grouped by region.
        // One entry per building; see InteriorConfig (Instances.cs) for what each field
        // does. Coordinates are world-space positions taken from the live game.
        public static List<InteriorConfig> SupportedInteriors = new List<InteriorConfig>
        {
            // MYSTERY LAKE:

            // 1. Camp Office
            new InteriorConfig
            {
                ExteriorSceneName = "LakeRegion",
                InteriorSceneBaseName = "CampOffice",
                ExteriorShellPrefabName = "STRSPAWN_CampOffice_Prefab",
                YOffset = 2f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1019.738f, 28.7883f, 440.6331f),
                ForceExactPosition = false,

                // EXPERIMENTAL, not for release: parks this one building away from its shell
                // so the behavior can be compared against every other building in the same
                // save, which stays in place. Set back to Vector3.zero to restore it.
                //
                // UP, not down: the game refuses underground player positions and recovers
                // the player to a fixed point on the surface, measured three times out of
                // three. Nothing is in the way below - the space there is empty - so this is
                // a rule about the player, not about geometry. Straight up also keeps every
                // clone above its own shell, so parked buildings stay as far apart as their
                // shells already are and no per-building site allocation is needed.
                // HOW FAR UP: only just clear of the shell, NOT hundreds of metres.
                //
                // 600m was measured on 2026-09-14 to put the world into a culled state. With the
                // player that far from the shell the game drops nearby scenery: of 181 renderers
                // within 60m of the shell, 61 that are visible from outside are hidden while the
                // player is inside, and another 32 are not in the scene at all. Zero go the other
                // way. That is the game's own culling and streaming, not the mod's auto-hide,
                // whose lists held 55 objects with none of them inactive.
                //
                // It is only visible once something looks out of the building. A window portal
                // renders the world from the shell's frame, so it renders that culled world: an
                // icicle survived while the rock it hangs on did not, leaving it floating.
                //
                // The interior is about 10.7m tall and the shell rather less, so 40m clears both
                // with room to spare while keeping the player close enough that nothing culls.
                // CONTROL RUN 2026-09-15: parked height set to ZERO, building on the ground.
                //
                // Isolates parking from the disappearing-trees bug. At 40m a live census measured
                // 14,099 of 21,064 in-scene renderers inactive, so "close enough that nothing
                // culls" above is not true as written. On the ground the portal delta collapses to
                // the interior-versus-shell window offset alone and the player is where the world
                // expects them, so if distant trees still drop out of a portal view here, parking
                // is exonerated and the cause is in the portal render itself.
                //
                // That control ran, and what it showed was the portal drawing the cabin's own walls.
                //
                // TEST RUN 2026-09-18: parked 40m up again, to reproduce the disappearing trees and
                // test the instanced-renderer explanation against them (see Main.cs OnLateUpdate).
                // That run found the cause (the game culls trees, rocks and wildlife for FPSCamera
                // alone; see ParkedCullEye.cs), so the census above was the game's culling answering
                // to the wrong eye, not a reason against parking.
                //
                // 2026-09-18, LATER: PARKING IS NOW THE DEFAULT FOR EVERY BUILDING (owner: "I think we
                // can set them all to 40 meters. We'll just have to go around and check each one."),
                // so this building no longer sets it itself; see InteriorConfig.ParkOffset. Everything
                // above was learned on the Camp Office and is why the default is what it is. A
                // building that turns out not to tolerate parking sets ParkOffset = Vector3.zero here.

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLight", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daytime" },
                ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                EntrySpawnPosition = new Vector3(1018.993f, 29.1481f, 444.3246f),
                ExitSpawnPosition = new Vector3(1019.351f, 28.1647f, 444.2208f),
                RotationOffset = Vector3.zero,
                DoorSpawnPoints = new List<DoorSpawnPoint>
                {
                    
                    new DoorSpawnPoint {
                    DoorName = "INTERACTIVE_CampOfficeInteriorDoorFront_Prefab",
                    DoorTransformPosition = new Vector3(1018.951f, 28.6588f, 445.1257f),
                    EntryPosition = new Vector3(1018.993f, 29.1481f, 444.3246f),
                    ExitPosition = new Vector3(1019.351f, 28.1647f, 444.2208f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "INTERACTIVE_CampOfficeInteriorDoorBack_Prefab",
                    DoorTransformPosition = new Vector3(1016.483f, 28.6588f, 435.9877f),
                    EntryPosition = new Vector3(1017.25f, 29.1261f, 436.3197f),
                    ExitPosition = new Vector3(1017.201f, 27.912f, 437.6288f)

                    },
                },
            },
            
            // 2. Trapper's Cabin
            new InteriorConfig
            {
                ExteriorSceneName = "LakeRegion",
                InteriorSceneBaseName = "SafeHouseA",
                ExteriorShellPrefabName = "STRSPAWN_CabinAExterior_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(37.8215f, 16.8949f, 22.6625f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLight", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daytime" },
                ObjectsToDisable = new List<string> { "Obj_WindowCabinABG", "Obj_WindowCabinBBG" },
                EntrySpawnPosition = new Vector3(37.1654f, 18.7259f, 28.0197f),
                ExitSpawnPosition = new Vector3(37.7069f, 17.5526f, 27.5437f),
                RotationOffset = new Vector3(0f, 90f, 0f),
            },

            // 3. LakeCabinA
            new InteriorConfig
            {
                 InstanceId = "LakeCabinA_1", 
                 ExteriorSceneName = "LakeRegion",
                 InteriorSceneBaseName = "LakeCabinA",  
                 ExteriorShellPrefabName = "STRSPAWN_LakeCabinA_Prefab",  
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(1478.075f, 21.0898f, -30.7528f),  
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow", "Obj_WindowCabinBBG" },
                 EntrySpawnPosition = new Vector3(1476.362f, 22.86f, -29.715f),
                 ExitSpawnPosition = new Vector3(1476.41f, 21.653f, -30.0735f),
                 RotationOffset = new Vector3(0f, 0f, 0f),
            },

            // 4. LakeCabinA
            new InteriorConfig
            {
                 InstanceId = "LakeCabinA_2",   
                 ExteriorSceneName = "LakeRegion",
                 InteriorSceneBaseName = "LakeCabinA",       
                 ExteriorShellPrefabName = "STRSPAWN_LakeCabinA_Prefab", 
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(1597.326f, 18.8208f, 48.1249f), 
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                 EntrySpawnPosition = new Vector3(1596.768f, 20.5929f, 50.0738f),
                 ExitSpawnPosition = new Vector3(1598.148f, 19.6257f, 50.4385f),
                 RotationOffset = new Vector3(0f, 45f, 0f),
            },

            // 5. LakeCabinA
            new InteriorConfig
            {
                 InstanceId = "LakeCabinA_3",   
                 ExteriorSceneName = "LakeRegion",
                 InteriorSceneBaseName = "LakeCabinA",     
                 ExteriorShellPrefabName = "STRSPAWN_LakeCabinA_Prefab",  
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(1676.427f, 20.0027f, 294.8809f),  
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                 EntrySpawnPosition = new Vector3(1674.609f, 21.7748f, 295.9812f),
                 ExitSpawnPosition = new Vector3(1674.267f, 20.8077f, 293.0156f),
                 RotationOffset = new Vector3(0f, 0f, 0f),
            },

            // 6. LakeCabinF
            new InteriorConfig
            {
                 InstanceId = "LakeCabinF_1",   
                 ExteriorSceneName = "LakeRegion",
                 InteriorSceneBaseName = "LakeCabinF",       
                 ExteriorShellPrefabName = "STRSPAWN_LakeCabinF_Prefab",  
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(1630.82f, 19.5224f, 79.1229f), 
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                 EntrySpawnPosition = new Vector3(1628.317f, 21.2894f, 80.4988f),
                 ExitSpawnPosition = new Vector3(1627.907f, 19.6558f, 81.6902f),
                 RotationOffset = new Vector3(0f, 120f, 0f),
            },

            // 7. LakeCabinE
            new InteriorConfig
            {
                 InstanceId = "LakeCabinE_1",   
                 ExteriorSceneName = "LakeRegion",
                 InteriorSceneBaseName = "LakeCabinE",       
                 ExteriorShellPrefabName = "STRSPAWN_LakeCabinE_Prefab", 
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(1617.083f, 19.3323f, 65.0903f), 
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                 EntrySpawnPosition = new Vector3(1615.743f, 21.1044f, 66.2781f),
                 ExitSpawnPosition = new Vector3(1615.258f, 19.7538f, 65.8742f),
                 RotationOffset = new Vector3(0f, 45f, 0f),
            },

            // 8. LakeCabinB
            new InteriorConfig
            {
                 InstanceId = "LakeCabinB_1", 
                 ExteriorSceneName = "LakeRegion",
                 InteriorSceneBaseName = "LakeCabinB",      
                 ExteriorShellPrefabName = "STRSPAWN_LakeCabinB_Prefab",  
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(1466.763f, 20.0836f, -45.2958f),  
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                 EntrySpawnPosition = new Vector3(1469.62f, 21.8336f, -45.2959f),
                 ExitSpawnPosition = new Vector3(1469.903f, 21.2886f, -44.5908f),
                 RotationOffset = new Vector3(0f, 270f, 0f),
            },

            // 9. LakeCabinD
            new InteriorConfig
            {
                 InstanceId = "LakeCabinD_1",  
                 ExteriorSceneName = "LakeRegion",
                 InteriorSceneBaseName = "LakeCabinD",    
                 ExteriorShellPrefabName = "STRSPAWN_LakeCabinD_Prefab", 
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(1452.142f, 20.3068f, -48.6879f),  
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                 EntrySpawnPosition = new Vector3(1452.271f, 22.0907f, -50.4605f),
                 ExitSpawnPosition = new Vector3(1452.652f, 21.2856f, -51.0189f),
                 RotationOffset = new Vector3(0f, 270f, 0f),
            },

            // 10. CampTrailerA
            new InteriorConfig
            {
                 InstanceId = "CampTrailerA_1",   
                 ExteriorSceneName = "LakeRegion",
                 InteriorSceneBaseName = "TrailerA",       
                 ExteriorShellPrefabName = "STRSPAWN_CampTrailerA_Prefab",  
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(894.9891f, 22.9289f, 1243.292f), 
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                 EntrySpawnPosition = new Vector3(892.7058f, 24.6789f, 1244.679f),
                 ExitSpawnPosition = new Vector3(895.1741f, 23.7277f, 1246.003f),
                 RotationOffset = new Vector3(0f, 0f, 0f),
            },

            // 11. CampTrailerB
            new InteriorConfig
            {
                 InstanceId = "CampTrailerB_1",
                 ExteriorSceneName = "LakeRegion",
                 InteriorSceneBaseName = "TrailerB",
                 ExteriorShellPrefabName = "STRSPAWN_CampTrailerB_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(918.3688f, 22.3791f, 1251.528f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                 EntrySpawnPosition = new Vector3(916.9804f, 24.1291f, 1256.361f),
                 ExitSpawnPosition = new Vector3(917.7195f, 23.5807f, 1255.464f),
                 RotationOffset = new Vector3(0f, 270f, 0f),
            },

            // 12. CampTrailerC
            new InteriorConfig
            {
                 InstanceId = "CampTrailerC_1",
                 ExteriorSceneName = "LakeRegion",
                 InteriorSceneBaseName = "TrailerC",
                 ExteriorShellPrefabName = "STRSPAWN_CampTrailerC_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(888.3475f, 22.9545f, 1270.186f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                 EntrySpawnPosition = new Vector3(889.0065f, 24.7045f, 1268.959f),
                 ExitSpawnPosition = new Vector3(889.191f, 23.5576f, 1268.111f),
                 RotationOffset = new Vector3(0f, 160f, 0f),
            },

            // 13. CampTrailerD
            new InteriorConfig
            {
                 InstanceId = "CampTrailerD_1",
                 ExteriorSceneName = "LakeRegion",
                 InteriorSceneBaseName = "TrailerD",
                 ExteriorShellPrefabName = "STR_CampTrailerDBase_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(1633.039f, 36.43f, 1253.3f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                 EntrySpawnPosition = new Vector3(1633.610f, 38.18f, 1255.657f),
                 ExitSpawnPosition = new Vector3(1633.274f, 37.3489f, 1254.832f),
                 RotationOffset = new Vector3(0f, 75f, 0f),
            },

            // 14. CampTrailerE
            new InteriorConfig
            {
                 InstanceId = "CampTrailerE_1",
                 ExteriorSceneName = "LakeRegion",
                 InteriorSceneBaseName = "DamTrailerB",
                 ExteriorShellPrefabName = "STR_CampTrailerEBase_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(1675.311f, 36.51f, 1249.83f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_E", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                 EntrySpawnPosition = new Vector3(1673.883f, 38.26f, 1254.62f),
                 ExitSpawnPosition = new Vector3(1679.078f, 37.8301f, 1255.038f),
                 RotationOffset = new Vector3(0f, 270f, 0f),
            },

            // 15. LakeCabinC_1
            new InteriorConfig
            {
                 InstanceId = "LakeCabinC_1",
                 ExteriorSceneName = "LakeRegion",
                 InteriorSceneBaseName = "LakeCabinC",
                 ExteriorShellPrefabName = "STRSPAWN_LakeCabinC_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(63.2956f, 26.6627f, 945.3474f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                 EntrySpawnPosition = new Vector3(65.0402f, 28.4348f, 943.9667f),
                 ExitSpawnPosition = new Vector3(65.7518f, 27.2677f, 945.5585f),
                 RotationOffset = new Vector3(0f, 180f, 0f),
            },

            //PLEASANT VALLEY:

            // 1. FarmHouse
            new InteriorConfig
            {
                ExteriorSceneName = "RuralRegion",
                InteriorSceneBaseName = "FarmHouseA",
                ExteriorShellPrefabName = "STRSPAWN_FarmHouseA_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1448.9f, 49.7f, 1027.8f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLight", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daytime", "Nighttime", "NightTime" },
                ObjectsToDisable = new List<string> { "STR_FarmHouseWindowGlowB", "STR_FarmHouseWindowGlowA", "STR_FarmHouseWindowGlow", "STR_FarmHouseParlourRoomWindowGlow_Prefab", "STR_FarmHouseWindowDoubleGlow" },
                EntrySpawnPosition = new Vector3(1452.277f, 51.5464f, 1033.05f),
                ExitSpawnPosition = new Vector3(1454.619f, 50.5074f, 1032.469f),
                RotationOffset = new Vector3(0f, 0f, 0f),
                DoorSpawnPoints = new List<DoorSpawnPoint>
                {
                    
                    new DoorSpawnPoint {
                    DoorName = "STR_FarmHouseDoor_Prefab",
                    DoorTransformPosition = new Vector3(1448.9f, 49.7f, 1020.762f),
                    EntryPosition = new Vector3(1448.931f, 51.5521f, 1022.264f),
                    ExitPosition = new Vector3(1447.433f, 50.5513f, 1020.128f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "STR_FarmHouseDoor_Prefab",
                    DoorTransformPosition = new Vector3(1444.637f, 49.7f, 1030.627f),
                    EntryPosition = new Vector3(1445.741f, 51.5521f, 1030.289f),
                    ExitPosition = new Vector3(1444.241f, 50.5513f, 1031.159f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "STR_FarmHouseDoor_Prefab",
                    DoorTransformPosition = new Vector3(1453.155f, 49.7f, 1033.919f),
                    EntryPosition = new Vector3(1452.277f, 51.5464f, 1033.05f),
                    ExitPosition = new Vector3(1454.619f, 50.5074f, 1032.469f)

                    },
                },
                // Transition from inside FarmHouse down into the basement scene
                SubInteriorLinks = new List<SubInteriorLink>
                {
                    new SubInteriorLink
                    {
                        TargetInstanceId = "FarmHouseABasement_1",
                        // TODO: refresh these by locating the basement door in-game
                        ParentDoorPosition = new Vector3(1450.43f, 50.723f, 1028.16f),
                        ChildSpawnPosition = new Vector3(1446.022f, 43.3751f, 1024.793f),
                        ParentSpawnPosition = new Vector3(1449.932f, 51.4499f, 1028.27f),
                    },
                },
            },

            // 2. FarmHouse Basement (a basement with its own outside entrance)
            new InteriorConfig
            {
                InstanceId = "FarmHouseABasement_1",
                ExteriorSceneName = "RuralRegion",
                InteriorSceneBaseName = "FarmHouseABasement",
                ExteriorShellPrefabName = "", 
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1448.7f, 41.5001f, 1027.8f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear" },
                ObjectsToDisable = new List<string> { "DarkLightingManager_Prefab" },
                EntrySpawnPosition = new Vector3(1450.644f, 43.2501f, 1032.571f),
                ExitSpawnPosition = new Vector3(1450.803f, 49.9805f, 1040.556f),
                RotationOffset = new Vector3(0f, 270f, 0f),
                DoorSpawnPoints = new List<DoorSpawnPoint>
                {
                    new DoorSpawnPoint {
                        DoorName = "STR_FarmHouseABasementDoor_Prefab",
                        DoorTransformPosition = new Vector3(1448.7f, 47.8f, 1027.8f),
                        EntryPosition = new Vector3(1450.644f, 43.2501f, 1032.571f),
                        ExitPosition = new Vector3(1450.803f, 49.9805f, 1040.556f)
                    },
                },
            },

            // 3. BarnSmall
            new InteriorConfig
            {
                 InstanceId = "BarnSmallA",
                 ExteriorSceneName = "RuralRegion",
                 InteriorSceneBaseName = "BarnHouseB",
                 ExteriorShellPrefabName = "STR_BarnSmallA_Exterior_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(1711.393f, 49.61f, 924.9883f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "DarkLightingManager_Prefab", "CONTAINER_InaccessibleGear" },
                 ObjectsToDisable = new List<string> { "STR_Barn_B_GlassGlow" },
                 EntrySpawnPosition = new Vector3(1711.84f, 51.4353f, 910.4988f),
                 ExitSpawnPosition = new Vector3(1718.404f, 50.6233f, 913.3584f),
                 RotationOffset = new Vector3(0f, 160f, 0f),
            },

            // 4. RadioControlHut
            new InteriorConfig
            {
                 InstanceId = "RadioControlHut",
                 ExteriorSceneName = "RuralRegion",
                 InteriorSceneBaseName = "RadioControlHut",
                 ExteriorShellPrefabName = "STRSPAWN_RadioControlHut_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(1915.77f, 200.9601f, 635.4105f),
                 ForceExactPosition = true,
                 // Switched off, not destroyed: the room's DarkLightingManager holds the glow planes and the
                 // day lights, and destroying them threw a null reference every frame (6,416 in one session,
                 // 2026-10-02; rebuilding its lists live stopped it). Off looks the same and keeps the references.
                 ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear" },
                 ObjectsToDisable = new List<string> { "STR_RadioControlHutInteriorWindowGlow_Prefab", "Beam Geometry", "Glow_Plane", "Glow_Plane (1)", "Glow_Plane (2)", "Day Lights" },
                 EntrySpawnPosition = new Vector3(1916.541f, 202.7901f, 631.2899f),
                 ExitSpawnPosition = new Vector3(1918.394f, 201.45f, 631.263f),
                 RotationOffset = new Vector3(0f, 50f, 0f),
            },

            // 5. Barn
            new InteriorConfig
            {
                 InstanceId = "BarnA",
                 ExteriorSceneName = "RuralRegion",
                 InteriorSceneBaseName = "BarnHouseA",
                 ExteriorShellPrefabName = "STRSPAWN_BarnHouseA_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(1675.61f, 43.42f, 1841.659f),
                 ForceExactPosition = true,
                 // InteriorLightingManager_Prefab as at every other building: with the lighting prefab
                 // destroyed, the second manager (under "Lighting (1)") threw a null reference every
                 // frame, 93,000 in one session (2026-09-30); switched off live, it stopped at once.
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "FX_LightShaft_C", "BarnHouseA_Lighting_Prefab", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear" },
                 ObjectsToDisable = new List<string> { "STR_BarnHouseAWindowsGlow_Prefab" },
                 EntrySpawnPosition = new Vector3(1675.586f, 45.17f, 1833.239f),
                 ExitSpawnPosition = new Vector3(1683.351f, 43.5867f, 1840.032f),
                 RotationOffset = new Vector3(0f, 325f, 0f),
                 DoorSpawnPoints = new List<DoorSpawnPoint>
                {

                    // Door and way-out positions read from the game's own objects (2026-09-30). As first
                    // written, neither DoorTransformPosition stood at a door (each was a copy of its entry
                    // position), both real doors were nearer the side entry, and both led to the same spot;
                    // the two ExitPositions were each other's. The game's triggers: front X 1677.27, Z 1828.16
                    // with BarnHouseAFrontExitPoint at X 1677.71, Z 1827.86; side X 1682.98, Z 1839.58 with
                    // BarnHouseASideExitPoint at X 1683.94, Z 1840.87. The exits keep the values first
                    // written, each moved to the door it stands at. The entries are as first written: with
                    // the room turned square to the barn, the front door's entry is by the room's front door.
                    new DoorSpawnPoint {
                    DoorName = "BarnHouseAFrontEnterPoint",
                    DoorTransformPosition = new Vector3(1677.27f, 43.65f, 1828.16f),
                    EntryPosition = new Vector3(1664.802f, 45.17f, 1845.277f),
                    ExitPosition = new Vector3(1677.822f, 43.5726f, 1827.994f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "BarnHouseASideEnterPoint",
                    DoorTransformPosition = new Vector3(1682.98f, 43.82f, 1839.58f),
                    EntryPosition = new Vector3(1675.586f, 45.17f, 1833.239f),
                    ExitPosition = new Vector3(1683.351f, 43.5867f, 1840.032f)

                    },
                }
            },

            //6. ThompsonShop
            new InteriorConfig
            {
                 InstanceId = "ThompsonStore",
                 ExteriorSceneName = "RuralRegion",
                 InteriorSceneBaseName = "RuralStoreA",
                 ExteriorShellPrefabName = "STR_Thompson_Store_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(2443.04f, 54.5864f, 2256.31f),
                 ForceExactPosition = true,
                 // InteriorLightingManager_Prefab as at Barn A: with the lighting prefab destroyed, the manager
                 // under "Lighting (1)" kept 30 destroyed lights and threw a null reference every frame
                 // (2026-10-02); switched off live, it stopped at once.
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "RuralStoreA_Lighting_Prefab", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear" },
                 ObjectsToDisable = new List<string> { "STR_RuralStoreAInteriorWindowsGlow_Prefab" },
                 EntrySpawnPosition = new Vector3(2439.719f, 56.4164f, 2253.688f),
                 ExitSpawnPosition = new Vector3(2438.583f, 55.0228f, 2255.389f),
                 RotationOffset = new Vector3(0f, 262.0424f, 0f),
                 DoorSpawnPoints = new List<DoorSpawnPoint>
                {

                    new DoorSpawnPoint {
                    DoorName = "FrontDoorInteriorExit",
                    DoorTransformPosition = new Vector3(2439.111f, 55.8784f, 2253.656f),
                    EntryPosition = new Vector3(2439.719f, 56.4164f, 2253.688f),
                    ExitPosition = new Vector3(2438.583f, 55.0228f, 2255.389f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "BackDoorInteriorExit",
                    DoorTransformPosition = new Vector3(2449.093f, 55.8764f, 2257.844f),
                    EntryPosition = new Vector3(2449.377f, 56.4164f, 2256.939f),
                    ExitPosition = new Vector3(2448.145f, 54.8628f, 2257.532f)

                    },
                }
            },

            //7. Community Hall
            new InteriorConfig
            {
                 InstanceId = "CommunityHall",
                 ExteriorSceneName = "RuralRegion",
                 InteriorSceneBaseName = "CommunityHallA",
                 ExteriorShellPrefabName = "STR_CommunityHallA_Exterior_Prefab (1)",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(2366.928f, 55.84f, 2258.868f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_E", "Light_CommunityHallA_Prefab", "CONTAINER_InaccessibleGear" },
                 ObjectsToDisable = new List<string> { "STR_CommunityHallA_WindowMatte" },
                 EntrySpawnPosition = new Vector3(2373.851f, 57.4354f, 2255.377f),
                 ExitSpawnPosition = new Vector3(2374.767f, 56.9728f, 2255.162f),
                 RotationOffset = new Vector3(0f, 17.8673f, 0f),
                 DoorSpawnPoints = new List<DoorSpawnPoint>
                {

                    new DoorSpawnPoint {
                    DoorName = "FrontDoorInteriorExit",
                    DoorTransformPosition = new Vector3(2375.626f, 56.606f, 2254.572f),
                    EntryPosition = new Vector3(2373.851f, 57.4354f, 2255.377f),
                    ExitPosition = new Vector3(2374.767f, 56.9728f, 2255.162f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "BackDoorInteriorExit",
                    DoorTransformPosition = new Vector3(2361.78f, 56.613f, 2265.771f),
                    EntryPosition = new Vector3(2363.052f, 57.4187f, 2264.541f),
                    ExitPosition = new Vector3(2365.669f, 56.8886f, 2268.47f)

                    },
                }
            },
            
            // 8. ThompsonHouse1
            new InteriorConfig
            {
                 InstanceId = "ThompsonHouse_1",
                 ExteriorSceneName = "RuralRegion",
                 InteriorSceneBaseName = "MiltonHouseA",
                 ExteriorShellPrefabName = "STRSPAWN_HouseExteriorA_Prefab (1)",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(2341.9f, 53.88f, 2211.561f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "MiltonHouseA_Lighting_Prefab", "CONTAINER_InaccessibleGear" },
                 ObjectsToDisable = new List<string> { "STR_CoastalHouseAWindowGlow_Prefab" },
                 EntrySpawnPosition = new Vector3(2341.937f, 55.7321f, 2216.193f),
                 ExitSpawnPosition = new Vector3(2342.267f, 55.103f, 2215.657f),
                 RotationOffset = new Vector3(0f, 90f, 0f),

            },

            //9. ThompsonHouse2
            new InteriorConfig
            {
                 InstanceId = "ThompsonHouse_2",
                 ExteriorSceneName = "RuralRegion",
                 InteriorSceneBaseName = "CoastalHouseD",
                 ExteriorShellPrefabName = "STRSPAWN_HouseExteriorD_Thomp_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(2285.409f, 56.2f, 2261.801f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "CoastalHouseD_Lighting_Prefab", "CONTAINER_InaccessibleGear" },
                 ObjectsToDisable = new List<string> { "STR_CoastalHouseDWindowGlow_Prefab" },
                 EntrySpawnPosition = new Vector3(2291.108f, 58.0521f, 2253.522f),
                 ExitSpawnPosition = new Vector3(2291.244f, 56.067f, 2255.125f),
                 RotationOffset = new Vector3(0f, 240, 0f),
                 DoorSpawnPoints = new List<DoorSpawnPoint>
                {

                    new DoorSpawnPoint {
                    DoorName = "FrontDoorExitCollider",
                    DoorTransformPosition = new Vector3(2291.422f, 57.3115f, 2253.076f),
                    EntryPosition = new Vector3(2291.108f, 58.0521f, 2253.522f),
                    ExitPosition = new Vector3(2291.085f, 56.1598f, 2254.99f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "BackDoorInteriorExitCollider",
                    DoorTransformPosition = new Vector3(2289.991f, 57.3115f, 2261.248f),
                    EntryPosition = new Vector3(2289.425f, 58.0521f, 2261.013f),
                    ExitPosition = new Vector3(2289.965f, 56.4982f, 2261.125f)

                    },
                }
            },

            // 10. ThompsonHouse3
            new InteriorConfig
            {
                 InstanceId = "ThompsonHouse_3",
                 ExteriorSceneName = "RuralRegion",
                 InteriorSceneBaseName = "CoastalHouseF",
                 ExteriorShellPrefabName = "STRSPAWN_HouseExteriorF_Thomp_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(2298.559f, 54f, 2245.528f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "CoastalHouseF_Lighting_Prefab", "CONTAINER_InaccessibleGear" },
                 ObjectsToDisable = new List<string> { "STR_CoastalHouseFWindowGlow_Prefab" },
                 EntrySpawnPosition = new Vector3(2300.927f, 55.8521f, 2250.822f),
                 ExitSpawnPosition = new Vector3(2300.884f, 55.5052f, 2250.901f),
                 RotationOffset = new Vector3(0f, 320f, 0f),

            },

            //11. ThompsonHouse4
            new InteriorConfig
            {
                 InstanceId = "ThompsonHouse_4",
                 ExteriorSceneName = "RuralRegion",
                 InteriorSceneBaseName = "CoastalHouseH",
                 ExteriorShellPrefabName = "STRSPAWN_HouseExteriorHRural_Tomp_Prefab Variant",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(2311.729f, 53.5f, 2268.92f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "CoastalHouseH_Lighting_Prefab", "CONTAINER_InaccessibleGear" },
                 ObjectsToDisable = new List<string> { "STR_CoastalHouseHWindowGlow_Prefab" },
                 EntrySpawnPosition = new Vector3(2308.653f, 55.13f, 2270.068f),
                 ExitSpawnPosition = new Vector3(2309.035f, 54.4062f, 2268.997f),
                 RotationOffset = new Vector3(0f, 270, 0f),
                 DoorSpawnPoints = new List<DoorSpawnPoint>
                {

                    new DoorSpawnPoint {
                    DoorName = "FrontDoorExitCollider",
                    DoorTransformPosition = new Vector3(2308.029f, 54.433f, 2270.104f),
                    EntryPosition = new Vector3(2308.655f, 55.13f, 2270.133f),
                    ExitPosition = new Vector3(2309.04f, 54.4886f, 2269.054f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "BackDoorInteriorExitCollider",
                    DoorTransformPosition = new Vector3(2310.8f, 54.433f, 2263.679f),
                    EntryPosition = new Vector3(2310.725f, 55.13f, 2264.305f),
                    ExitPosition = new Vector3(2313.518f, 54.6897f, 2265.395f)

                    },
                }
            },

            // 12. Thompson Church
            new InteriorConfig
            {
                 InstanceId = "ThompsonChurch",
                 ExteriorSceneName = "RuralRegion",
                 InteriorSceneBaseName = "ChurchC",
                 ExteriorShellPrefabName = "STR_ChurchC_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(2266.723f, 55.53f, 2286.753f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_E", "ChurchC_Lighting_Prefab", "CONTAINER_InaccessibleGear" },
                 ObjectsToDisable = new List<string> { "STR_ChurchCGlowMesh_Prefab", "STR_ChurchCInt_Glass_Prefab" },
                 EntrySpawnPosition = new Vector3(2270.964f, 57.3307f, 2286.781f),
                 ExitSpawnPosition = new Vector3(2272.547f, 57.6147f, 2288.157f),
                 RotationOffset = new Vector3(0f, 180f, 0f),

            },

            // 13. Forest Cabin
            new InteriorConfig
            {
                 InstanceId = "ForestCabin",
                 ExteriorSceneName = "RuralRegion",
                 InteriorSceneBaseName = "FishingCabinA",
                 ExteriorShellPrefabName = "STRSPAWN_LakeCabinA_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(940.7402f, 38.17f, 1670.38f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "FishingCabinA_Prefab", "CONTAINER_InaccessibleGear" },
                 ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                 EntrySpawnPosition = new Vector3(942.5097f, 40.0221f, 1669.246f),
                 ExitSpawnPosition = new Vector3(943.7504f, 38.955f, 1669.042f),
                 RotationOffset = new Vector3(0f, 180f, 0f),

            },
            // 14. CoastalHouseA (SIConfigTool 2026-10-04: 1 door(s), lift 1.35 m)
            new InteriorConfig
            {
                InstanceId = "ThompsonHouse_5",
                ExteriorSceneName = "RuralRegion",
                InteriorSceneBaseName = "CoastalHouseA",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorA_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(2474.483f, 56.419f, 2431.558f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "CoastalHouseA_Lighting_Prefab", "CONTAINER_InaccessibleGear" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseAWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 0f, 0f),
            },


            //COASTAL HIGHWAY:

            // 1. Quonset
            new InteriorConfig
            {
                ExteriorSceneName = "CoastalRegion",
                InteriorSceneBaseName = "QuonsetGasStation",
                ExteriorShellPrefabName = "STRSPAWN_QuonsetGasStation_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(772.6199f, 25.58f, 648.0303f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLight", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daytime" },
                ObjectsToDisable = new List<string> { "STR_GarageWindowsGlow_Prefab" },
                EntrySpawnPosition = new Vector3(762.0394f, 27.41f, 646.7645f),
                ExitSpawnPosition = new Vector3(761.3817f, 25.7838f, 646.5969f),
                RotationOffset = new Vector3(0f, 225f, 0f),
                DoorSpawnPoints = new List<DoorSpawnPoint>
                {
                    
                    new DoorSpawnPoint {
                    DoorName = "GasStationFrontEnterPoint",
                    DoorTransformPosition = new Vector3(762.0077f, 26.5441f, 647.0829f),
                    EntryPosition = new Vector3(762.0394f, 27.41f, 646.7645f),
                    ExitPosition = new Vector3(761.3817f, 25.7838f, 646.5969f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "GasStationBackEnterPoint",
                    DoorTransformPosition = new Vector3(775.7552f, 26.3226f, 655.2833f),
                    EntryPosition = new Vector3(775.9792f, 27.42f, 655.2222f),
                    ExitPosition = new Vector3(775.7437f, 25.7775f, 657.7051f)

                    },
                },
            },

            // 2. Coastal House
            new InteriorConfig
            {
                InstanceId = "CoastalHouse_1",
                ExteriorSceneName = "CoastalRegion",
                InteriorSceneBaseName = "CoastalHouseA",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorA_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(816.7708f, 26.8216f, 675.7695f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "CoastalHouseA_Lighting_Prefab", "CONTAINER_InaccessibleGear" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseAWindowGlow_Prefab" },
                EntrySpawnPosition = new Vector3(815.4048f, 28.6737f, 679.5962f),
                ExitSpawnPosition = new Vector3(815.1755f, 27.4621f, 680.5235f),
                RotationOffset = new Vector3(0f, 70f, 0f),
            },

            // 3. Coastal House
            new InteriorConfig
            {
                InstanceId = "CoastalHouse_2",
                ExteriorSceneName = "CoastalRegion",
                InteriorSceneBaseName = "CoastalHouseB",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorB_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(863.8964f, 27.1491f, 582.6648f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_B (1)", "FX_LightShaft_E", "CoastalHouse_B_Lighting_Prefab", "CONTAINER_InaccessibleGear" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseBWindowGlow_Prefab" },
                EntrySpawnPosition = new Vector3(863.6965f, 25.2491f, 582.2649f),
                ExitSpawnPosition = new Vector3(858.0344f, 28.1529f, 587.3011f),
                RotationOffset = new Vector3(0f, 322.7789f, 0f),
                DoorSpawnPoints = new List<DoorSpawnPoint>
                {

                    new DoorSpawnPoint {
                    DoorName = "FrontDoorExitCollider",
                    DoorTransformPosition = new Vector3(859.7379f, 28.2558f, 579.5021f),
                    EntryPosition = new Vector3(860.2061f, 29.0012f, 579.8844f),
                    ExitPosition = new Vector3(859.6492f, 27.9727f, 579.3168f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "BackDoorExitCollider",
                    DoorTransformPosition = new Vector3(857.1047f, 28.2558f, 588.8203f),
                    EntryPosition = new Vector3(857.4016f, 29.0012f, 588.3153f),
                    ExitPosition = new Vector3(858.1386f, 28.2344f, 587.3943f)

                    },
                }
            },

            // 4. Coastal House
            new InteriorConfig
            {
                InstanceId = "CoastalHouse_3",
                ExteriorSceneName = "CoastalRegion",
                InteriorSceneBaseName = "CoastalHouseC",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorC_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(824.8581f, 26.3498f, 604.3293f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "CoastalHouseC_Lighting_Prefab", "CONTAINER_InaccessibleGear" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseCWindowGlow_Prefab" },
                EntrySpawnPosition = new Vector3(826.3577f, 24.5498f, 606.1289f),
                ExitSpawnPosition = new Vector3(827.4914f, 26.9234f, 613.829f),
                RotationOffset = new Vector3(0f, 153.1841f, 0f),
                DoorSpawnPoints = new List<DoorSpawnPoint>
                {

                    new DoorSpawnPoint {
                    DoorName = "FrontDoorExitCollider",
                    DoorTransformPosition = new Vector3(830.4583f, 27.4702f, 604.8818f),
                    EntryPosition = new Vector3(829.9447f, 28.2019f, 604.5207f),
                    ExitPosition = new Vector3(831.4586f, 26.8295f, 606.4385f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "BackDoorExitCollider",
                    DoorTransformPosition = new Vector3(827.8194f, 27.4702f, 613.3392f),
                    EntryPosition = new Vector3(828.1426f, 28.1798f, 612.8f),
                    ExitPosition = new Vector3(827.5281f, 26.9966f, 613.8232f)

                    },
                }
            },

            // 5. Coastal House 
            new InteriorConfig
            {
                InstanceId = "CoastalHouse_4",
                ExteriorSceneName = "CoastalRegion",
                InteriorSceneBaseName = "CoastalHouseC",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorC_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(779.8056f, 27.5008f, 704.9326f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "CoastalHouseC_Lighting_Prefab", "CONTAINER_InaccessibleGear" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseCWindowGlow_Prefab" },
                EntrySpawnPosition = new Vector3(778.9058f, 25.2008f, 702.6331f),
                ExitSpawnPosition = new Vector3(779.5317f, 27.0772f, 694.9141f),
                RotationOffset = new Vector3(0f, 320f, 0f),
                DoorSpawnPoints = new List<DoorSpawnPoint>
                {

                    new DoorSpawnPoint {
                    DoorName = "FrontDoorExitCollider",
                    DoorTransformPosition = new Vector3(774.479f, 28.6212f, 703.1174f),
                    EntryPosition = new Vector3(775.0165f, 29.3529f, 703.4276f),
                    ExitPosition = new Vector3(774.0581f, 27.1732f, 701.1549f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "BackDoorExitCollider",
                    DoorTransformPosition = new Vector3(778.9774f, 28.6212f, 695.4847f),
                    EntryPosition = new Vector3(778.5887f, 29.3308f, 695.9797f),
                    ExitPosition = new Vector3(779.465f, 27.1487f, 694.8745f)

                    },
                }
            },

            // 6. Coastal House
            new InteriorConfig
            {
                InstanceId = "CoastalHouse_5",
                ExteriorSceneName = "CoastalRegion",
                InteriorSceneBaseName = "CoastalHouseA",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorA_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(726.0771f, 25.9805f, 721.9538f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "CoastalHouseA_Lighting_Prefab", "CONTAINER_InaccessibleGear" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseAWindowGlow_Prefab" },
                EntrySpawnPosition = new Vector3(722.4268f, 27.8326f, 719.1019f),
                ExitSpawnPosition = new Vector3(722.0412f, 26.3217f, 718.3496f),
                RotationOffset = new Vector3(0f, 321.3171f, 0f),
            },

            // 7. Coastal House
            new InteriorConfig
            {
                InstanceId = "CoastalHouse_6",
                ExteriorSceneName = "CoastalRegion",
                InteriorSceneBaseName = "CoastalHouseE",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorE_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(767.9241f, 26.1269f, 754.658f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_C", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daytime", "Midday" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseEWindowGlow_Prefab" },
                EntrySpawnPosition = new Vector3(769.7935f, 27.979f, 758.5815f),
                ExitSpawnPosition = new Vector3(770.9746f, 26.9453f, 759.1284f),
                RotationOffset = new Vector3(0f, 250f, 0f),
            },

            // 8. Coastal House 
            new InteriorConfig
            {
                InstanceId = "CoastalHouse_7",
                ExteriorSceneName = "CoastalRegion",
                InteriorSceneBaseName = "CoastalHouseC",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorC_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(727.7393f, 26.38f, 589.0101f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "CoastalHouseC_Lighting_Prefab", "CONTAINER_InaccessibleGear" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseCWindowGlow_Prefab" },
                EntrySpawnPosition = new Vector3(725.04f, 24.38f, 589.9099f),
                ExitSpawnPosition = new Vector3(718.4892f, 26.3029f, 585.816f),
                RotationOffset = new Vector3(0f, 28.4475f, 0f),
                DoorSpawnPoints = new List<DoorSpawnPoint>
                {

                    new DoorSpawnPoint {
                    DoorName = "FrontDoorExitCollider",
                    DoorTransformPosition = new Vector3(724.0943f, 27.5004f, 593.2975f),
                    EntryPosition = new Vector3(725.317f, 28.21f, 592.45f),
                    ExitPosition = new Vector3(721.5091f, 26.1501f, 593.5695f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "BackDoorExitCollider",
                    DoorTransformPosition = new Vector3(718.6478f, 27.5004f, 586.3097f),
                    EntryPosition = new Vector3(718.9332f, 28.21f, 586.8604f),
                    ExitPosition = new Vector3(718.5116f, 26.3913f, 585.8107f)

                    },
                }
            },

            // 9. Coastal House 
            new InteriorConfig
            {
                InstanceId = "CoastalHouse_8",
                ExteriorSceneName = "CoastalRegion",
                InteriorSceneBaseName = "CoastalHouseF",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorF_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(719.935f, 26.1788f, 549.7385f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_E", "FX_LightShaft_B", "CoastalHouseF_Lighting_Prefab", "CONTAINER_InaccessibleGear", },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseFWindowGlow_Prefab" },
                EntrySpawnPosition = new Vector3(714.713f, 28.0309f, 547.075f),
                ExitSpawnPosition = new Vector3(720.7502f, 26.2444f, 549.8903f),
                RotationOffset = new Vector3(0f, 180f, 0f),
            },

            // 10. Coastal House
            new InteriorConfig
            {
                InstanceId = "CoastalHouse_9",
                ExteriorSceneName = "CoastalRegion",
                InteriorSceneBaseName = "CoastalHouseA",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorA_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(765.3765f, 25.4085f, 576.5338f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "CoastalHouseA_Lighting_Prefab", "CONTAINER_InaccessibleGear" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseAWindowGlow_Prefab" },
                EntrySpawnPosition = new Vector3(768.324f, 27.2606f, 572.9363f),
                ExitSpawnPosition = new Vector3(768.5502f, 26.2285f, 575.2491f),
                RotationOffset = new Vector3(0f, 230f, 0f),
            },

            //11. Coastal House 
            new InteriorConfig
            {
                InstanceId = "CoastalHouse_10",
                ExteriorSceneName = "CoastalRegion",
                InteriorSceneBaseName = "CoastalHouseB",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorB_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(686.9126f, 26.6491f, 639.7988f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_B (1)", "FX_LightShaft_E", "CoastalHouse_B_Lighting_Prefab", "CONTAINER_InaccessibleGear" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseBWindowGlow_Prefab" },
                EntrySpawnPosition = new Vector3(686.9126f, 25.2491f, 640.7986f),
                ExitSpawnPosition = new Vector3(681.313f, 28.2316f, 635.7045f),
                RotationOffset = new Vector3(0f, 234.9675f, 0f),
                DoorSpawnPoints = new List<DoorSpawnPoint>
                {

                    new DoorSpawnPoint {
                    DoorName = "FrontDoorExitCollider",
                    DoorTransformPosition = new Vector3(689.9141f, 27.7557f, 635.5226f),
                    EntryPosition = new Vector3(689.5259f, 28.5012f, 635.9818f),
                    ExitPosition = new Vector3(689.5574f, 27.9772f, 636.5552f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "BackDoorExitCollider",
                    DoorTransformPosition = new Vector3(680.5023f, 27.7557f, 633.2471f),
                    EntryPosition = new Vector3(681.0156f, 28.5012f, 633.5239f),
                    ExitPosition = new Vector3(681.2908f, 28.2384f, 635.7122f)

                    },
                }
            },

            // 12. Coastal House
            new InteriorConfig
            {
                InstanceId = "CoastalHouse_11",
                ExteriorSceneName = "CoastalRegion",
                InteriorSceneBaseName = "CoastalHouseE",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorE_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(703.503f, 31.4127f, -90.3462f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_C", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daytime", "Midday" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseEWindowGlow_Prefab" },
                EntrySpawnPosition = new Vector3(705.726f, 33.2648f, -93.96f),
                ExitSpawnPosition = new Vector3(705.7604f, 32.4901f, -95.266f),
                RotationOffset = new Vector3(0f, 10.7255f, 0f),
            },

            //13. Coastal House 
            new InteriorConfig
            {
                InstanceId = "CoastalHouse_12", //BİTTİ ///////////////////////////////////////////////////////
                ExteriorSceneName = "CoastalRegion",
                InteriorSceneBaseName = "CoastalHouseB",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorB_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(706.7186f, 32.3186f, -155.2179f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_B (1)", "FX_LightShaft_E", "CoastalHouse_B_Lighting_Prefab", "CONTAINER_InaccessibleGear" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseBWindowGlow_Prefab" },
                EntrySpawnPosition = new Vector3(686.9126f, 25.2491f, 640.7986f),
                ExitSpawnPosition = new Vector3(701.0604f, 33.4109f, -156.9868f),
                RotationOffset = new Vector3(0f, 266.3776f, 0f),
                DoorSpawnPoints = new List<DoorSpawnPoint>
                {

                    new DoorSpawnPoint {
                    DoorName = "FrontDoorExitCollider",
                    DoorTransformPosition = new Vector3(707.0517f, 33.4252f, -160.4318f),
                    EntryPosition = new Vector3(707.0333f, 34.1707f, -159.8185f),
                    ExitPosition = new Vector3(708.7142f, 33.1421f, -160.1186f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "BackDoorExitCollider",
                    DoorTransformPosition = new Vector3(697.8332f, 33.4252f, -157.4687f),
                    EntryPosition = new Vector3(698.4178f, 34.1707f, -157.4171f),
                    ExitPosition = new Vector3(701.0771f, 33.4014f, -156.9636f)

                    },
                }
            },

            // 14. Coastal House 
            new InteriorConfig
            {
                InstanceId = "CoastalHouse_13",
                ExteriorSceneName = "CoastalRegion",
                InteriorSceneBaseName = "CoastalHouseF",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorF_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(666.0112f, 31.3049f, -141.1522f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_E", "FX_LightShaft_B", "CoastalHouseF_Lighting_Prefab", "CONTAINER_InaccessibleGear", },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseFWindowGlow_Prefab" },
                EntrySpawnPosition = new Vector3(671.2271f, 33.157f, -138.5155f),
                ExitSpawnPosition = new Vector3(672.4543f, 31.8323f, -138.6627f),
                RotationOffset = new Vector3(0f, 0f, 0f),
            },

            // 15. Coastal House
            new InteriorConfig
            {
                InstanceId = "CoastalHouse_14",
                ExteriorSceneName = "CoastalRegion",
                InteriorSceneBaseName = "CoastalHouseE",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorE_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(942.0154f, 49.1201f, 499.9244f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_C", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daytime", "Midday" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseEWindowGlow_Prefab" },
                EntrySpawnPosition = new Vector3(943.9118f, 50.8922f, 503.7778f),
                ExitSpawnPosition = new Vector3(945.0051f, 50.3745f, 504.2267f),
                RotationOffset = new Vector3(0f, 250f, 0f),
            },

            // 16. Camp Trailer
            new InteriorConfig
            {
                 InstanceId = "CampTrailerB_2",
                 ExteriorSceneName = "CoastalRegion",
                 InteriorSceneBaseName = "TrailerB",
                 ExteriorShellPrefabName = "STRSPAWN_CampTrailerB_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(829.5067f, 86.9032f, 913.3613f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                 EntrySpawnPosition = new Vector3(825.031f, 88.6532f, 915.2846f),
                 ExitSpawnPosition = new Vector3(824.5615f, 87.2018f, 914.7861f),
                 RotationOffset = new Vector3(0f, 220f, 0f),
            },

            // 17. Lake Cabin
            new InteriorConfig
            {
                 InstanceId = "LakeCabinC_2",
                 ExteriorSceneName = "CoastalRegion",
                 InteriorSceneBaseName = "LakeCabinC",
                 ExteriorShellPrefabName = "STRSPAWN_LakeCabinC_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(351.5066f, 87.1127f, 1026.092f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                 EntrySpawnPosition = new Vector3(352.912f, 88.8848f, 1027.881f),
                 ExitSpawnPosition = new Vector3(351.9158f, 88.1976f, 1028.517f),
                 RotationOffset = new Vector3(0f, 90f, 0f),
            },

            // 18. Lake Cabin
            new InteriorConfig
            {
                 InstanceId = "LakeCabinC_3",
                 ExteriorSceneName = "CoastalRegion",
                 InteriorSceneBaseName = "LakeCabinC",
                 ExteriorShellPrefabName = "STRSPAWN_LakeCabinC_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(336.7627f, 86.7468f, 1025.875f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                 EntrySpawnPosition = new Vector3(338.0737f, 88.5989f, 1027.633f),
                 ExitSpawnPosition = new Vector3(336.8512f, 88.1317f, 1028.26f),
                 RotationOffset = new Vector3(0f, 90f, 0f),
            },

            // 19. LakeCabin
            new InteriorConfig
            {
                 InstanceId = "LakeCabinF_2",
                 ExteriorSceneName = "CoastalRegion",
                 InteriorSceneBaseName = "LakeCabinF",
                 ExteriorShellPrefabName = "STRSPAWN_LakeCabinF_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(337.6167f, 87.68f, 1060.673f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                 EntrySpawnPosition = new Vector3(337.5035f, 89.527f, 1057.818f),
                 ExitSpawnPosition = new Vector3(337.8156f, 88.1649f, 1058.483f),
                 RotationOffset = new Vector3(0f, 0f, 0f),
            },

            // 20. LakeCabin
            new InteriorConfig
            {
                 InstanceId = "LakeCabinE",
                 ExteriorSceneName = "CoastalRegion",
                 InteriorSceneBaseName = "LakeCabinE",
                 ExteriorShellPrefabName = "STRSPAWN_LakeCabinE_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(325.5593f, 87.5984f, 1063.775f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "LakeCabinE_Prefab", "CONTAINER_InaccessibleGear" },
                 ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                 EntrySpawnPosition = new Vector3(325.5246f, 89.4504f, 1062.001f),
                 ExitSpawnPosition = new Vector3(325.7714f, 88.0971f, 1061.371f),
                 RotationOffset = new Vector3(0f, 270f, 0f),
            },

            // 21. CampTrailer
            new InteriorConfig
            {
                 InstanceId = "CampTrailerC_2",
                 ExteriorSceneName = "CoastalRegion",
                 InteriorSceneBaseName = "TrailerC",
                 ExteriorShellPrefabName = "STRSPAWN_CampTrailerC_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(-235.0721f, 121.9007f, 1148.722f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                 EntrySpawnPosition = new Vector3(-234.2166f, 123.7307f, 1147.62f),
                 ExitSpawnPosition = new Vector3(-233.7691f, 122.6004f, 1146.785f),
                 RotationOffset = new Vector3(0f, 148.3544f, 0f),
            },

            // 22. Coastal House 
            new InteriorConfig
            {
                InstanceId = "CoastalHouse_15",
                ExteriorSceneName = "CoastalRegion",
                InteriorSceneBaseName = "CoastalHouseF",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorF_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(-82.2333f, 26.1395f, 903.1144f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_E", "FX_LightShaft_B", "CoastalHouseF_Lighting_Prefab", "CONTAINER_InaccessibleGear", },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseFWindowGlow_Prefab" },
                EntrySpawnPosition = new Vector3(-79.3302f, 27.9916f, 908.1478f),
                ExitSpawnPosition = new Vector3(-78.2415f, 26.6952f, 908.7671f),
                RotationOffset = new Vector3(0f, 325.967f, 0f),
            },

            // 23. CampTrailer 
            new InteriorConfig
            {
                 InstanceId = "CampTrailerA_2",
                 ExteriorSceneName = "CoastalRegion",
                 InteriorSceneBaseName = "TrailerA",
                 ExteriorShellPrefabName = "STRSPAWN_CampTrailerA_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(-39.1301f, 26.42f, 867.1301f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                 EntrySpawnPosition = new Vector3(-39.7146f, 28.25f, 869.7007f),
                 ExitSpawnPosition = new Vector3(-39.3958f, 26.9079f, 870.6727f),
                 RotationOffset = new Vector3(0f, 45.1599f, 0f),
            },

            // 24. LakeCabin
            new InteriorConfig
            {
                 InstanceId = "LakeCabinD_2",
                 ExteriorSceneName = "CoastalRegion",
                 InteriorSceneBaseName = "LakeCabinD",
                 ExteriorShellPrefabName = "STRSPAWN_LakeCabinD_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(-488.84f, 100.765f, 1106.3f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                 EntrySpawnPosition = new Vector3(-490.4361f, 102.6289f, 1105.53f),
                 ExitSpawnPosition = new Vector3(-491.3864f, 101.1343f, 1105.136f),
                 RotationOffset = new Vector3(0f, 337.6966f, 0f),
            },

            // 25. CampTrailer 
            new InteriorConfig
            {
                 InstanceId = "CampTrailerA_3",
                 ExteriorSceneName = "CoastalRegion",
                 InteriorSceneBaseName = "TrailerA",
                 ExteriorShellPrefabName = "STRSPAWN_CampTrailerA_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(-808.21f, 125.83f, 1013.88f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                 EntrySpawnPosition = new Vector3(-806.8217f, 127.66f, 1016.205f),
                 ExitSpawnPosition = new Vector3(-806.0568f, 126.8285f, 1015.51f),
                 RotationOffset = new Vector3(0f, 90f, 0f),
            },

            // 26. CampTrailer
            new InteriorConfig
            {
                 InstanceId = "CampTrailerC_3",
                 ExteriorSceneName = "CoastalRegion",
                 InteriorSceneBaseName = "TrailerC",
                 ExteriorShellPrefabName = "STRSPAWN_CampTrailerC_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(-746.3333f, 205.2566f, 1262.638f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                 EntrySpawnPosition = new Vector3(-747.2574f, 207.0866f, 1263.668f),
                 ExitSpawnPosition = new Vector3(-747.4169f, 206.3574f, 1263.978f),
                 RotationOffset = new Vector3(0f, 320.2021f, 0f),
            },

            // 27. LakeCabin
            new InteriorConfig
            {
                 InstanceId = "LakeCabinD_3",
                 ExteriorSceneName = "CoastalRegion",
                 InteriorSceneBaseName = "LakeCabinD",
                 ExteriorShellPrefabName = "STRSPAWN_LakeCabinD_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(-921.8821f, 72.186f, 658.2991f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                 EntrySpawnPosition = new Vector3(-921.9888f, 74.0499f, 660.0662f),
                 ExitSpawnPosition = new Vector3(-922.0863f, 72.6304f, 660.687f),
                 RotationOffset = new Vector3(0f, 90f, 0f),
            },

            // 28. LakeCabin
            new InteriorConfig
            {
                 InstanceId = "LakeCabinD_4",
                 ExteriorSceneName = "CoastalRegion",
                 InteriorSceneBaseName = "FishingCabinD",
                 ExteriorShellPrefabName = "STRSPAWN_LakeCabinD_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(-751.7756f, 28.792f, 660.6635f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                 EntrySpawnPosition = new Vector3(-751.2855f, 30.5759f, 662.3611f),
                 ExitSpawnPosition = new Vector3(-751.1953f, 29.3918f, 662.4866f),
                 RotationOffset = new Vector3(0f, 105f, 0f),
            },

            // 29. LakeCabin
            new InteriorConfig
            {
                 InstanceId = "LakeCabinA_4",
                 ExteriorSceneName = "CoastalRegion",
                 InteriorSceneBaseName = "FishingCabinA",
                 ExteriorShellPrefabName = "STRSPAWN_LakeCabinA_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(-710.3699f, 27.72f, 662.6203f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                 EntrySpawnPosition = new Vector3(-710.4674f, 29.4921f, 664.7117f),
                 ExitSpawnPosition = new Vector3(-710.861f, 29.3226f, 665.4387f),
                 RotationOffset = new Vector3(0f, 55f, 0f),
            },

            // 30. Lake Cabin
            new InteriorConfig
            {
                 InstanceId = "LakeCabinC_4",
                 ExteriorSceneName = "CoastalRegion",
                 InteriorSceneBaseName = "FishingCabinC",
                 ExteriorShellPrefabName = "STRSPAWN_LakeCabinC_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(-700.8101f, 28.58f, 669.5401f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                 EntrySpawnPosition = new Vector3(-700.6646f, 30.3521f, 671.7798f),
                 ExitSpawnPosition = new Vector3(-702.4091f, 29.5649f, 671.7916f),
                 RotationOffset = new Vector3(0f, 55f, 0f),
            },

            // 31. LakeCabinB
            new InteriorConfig
            {
                 InstanceId = "LakeCabinB_2",
                 ExteriorSceneName = "CoastalRegion",
                 InteriorSceneBaseName = "LakeCabinB",
                 ExteriorShellPrefabName = "STRSPAWN_LakeCabinB_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(-690.45f, 27.64f, 676.05f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                 ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                 EntrySpawnPosition = new Vector3(-691.8919f, 29.47f, 678.5127f),
                 ExitSpawnPosition = new Vector3(-692.0897f, 29.2249f, 678.8051f),
                 RotationOffset = new Vector3(0f, 150f, 0f),
            },

            //32. Coastal House 
            new InteriorConfig
            {
                InstanceId = "CoastalHouse_16",
                ExteriorSceneName = "CoastalRegion",
                InteriorSceneBaseName = "CoastalHouseD",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorD_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(-273.104f, 76.2579f, 280.2721f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_B (1)", "FX_LightShaft_E", "CoastalHouse_B_Lighting_Prefab", "CONTAINER_InaccessibleGear" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseDWindowGlow_Prefab" },
                EntrySpawnPosition = new Vector3(-271.6039f, 74.5579f, 284.0723f),
                ExitSpawnPosition = new Vector3(-272.0056f, 76.9111f, 291.7282f),
                RotationOffset = new Vector3(0f, 90f, 0f),
                DoorSpawnPoints = new List<DoorSpawnPoint>
                {

                    new DoorSpawnPoint {
                    DoorName = "FrontDoorExitCollider",
                    DoorTransformPosition = new Vector3(-273.9495f, 77.3694f, 290.8345f),
                    EntryPosition = new Vector3(-273.9495f, 78.11f, 290.2255f),
                    ExitPosition = new Vector3(-272.0056f, 76.9111f, 291.7282f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "BackDoorExitCollider",
                    DoorTransformPosition = new Vector3(-276.7951f, 77.3694f, 283.0423f),
                    EntryPosition = new Vector3(-276.1878f, 78.11f, 282.9903f),
                    ExitPosition = new Vector3(-275.5518f, 77.0709f, 286.3218f)

                    },
                }
            },

            // 33. Coastal House
            new InteriorConfig
            {
                InstanceId = "CoastalHouse_17",
                ExteriorSceneName = "CoastalRegion",
                InteriorSceneBaseName = "CoastalHouseE",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorE_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(365.5192f, 58.4654f, 219.2763f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_C", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daytime", "Midday" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseEWindowGlow_Prefab" },
                EntrySpawnPosition = new Vector3(365.383f, 60.3175f, 223.5576f),
                ExitSpawnPosition = new Vector3(366.2148f, 59.0428f, 224.5376f),
                RotationOffset = new Vector3(0f, 221.5674f, 0f),
            },
            // 34. CoastalHouseH (SIConfigTool 2026-10-04: 2 door(s), lift 1.10 m)
            new InteriorConfig
            {
                InstanceId = "CoastalHouse_18",
                ExteriorSceneName = "CoastalRegion",
                InteriorSceneBaseName = "CoastalHouseH",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorH_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(-61.9877f, 26.4062f, 880.5861f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "CoastalHouseH_Lighting_Prefab", "CONTAINER_InaccessibleGear" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseHWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 61.68f, 0f),
            },

            // 35. FishingCabinD (SIConfigTool 2026-10-04: 1 door(s), lift 2.50 m)
            new InteriorConfig
            {
                InstanceId = "LakeCabinD_5",
                ExteriorSceneName = "CoastalRegion",
                InteriorSceneBaseName = "FishingCabinD",
                ExteriorShellPrefabName = "STRSPAWN_LakeCabinD_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(-1224.771f, 130.3257f, 984.3444f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                RotationOffset = new Vector3(0f, 281.75f, 0f),
            },


            //FORSAKEN AIRFIELD:

            // 1. Hangar
            new InteriorConfig
            {
                ExteriorSceneName = "AirfieldRegion",
                InteriorSceneBaseName = "AFHangar",
                ExteriorShellPrefabName = "STR_AF_Hangar_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(161.8379f, 161.5894f, -649.4945f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear", "DLM_AFHangar_LGT_Prefab" },
                // The painted outside behind the END wall stays off: real views stand there (see
                // s_BuildingWindowOverrides). The one behind the two leaning SIDE bands is left ON, as
                // the game has it, because those bands get no view and without it they show open sky
                // from where the room is parked (owner, 2026-09-20: "All the windows are 40m up").
                ObjectsToDisable = new List<string> { "STR_AF_Hangar_Interior_Main_Windows_Glow_Prefab" },
                EntrySpawnPosition = new Vector3(162.8583f, 162.56f, -618.6203f),
                ExitSpawnPosition = new Vector3(163.1478f, 161.594f, -617.2457f),
                RotationOffset = new Vector3(0f, 300f, 0f),
                TerrainHoleSize = new Vector3(27f, 10f, 50f), // hole size (X width, Y depth, Z length)
                TerrainHoleOffset = new Vector3(166.4297f, 158.3126f, -642.0966f), // hole centre in world space
                DoorSpawnPoints = new List<DoorSpawnPoint>
                {
                    
                    new DoorSpawnPoint {
                    DoorName = "STR_MetalDoorExt_E_Left_Prefab",
                    DoorTransformPosition = new Vector3(163.1364f, 160.9523f, -618.2877f),
                    EntryPosition = new Vector3(162.3004f, 163.3348f, -618.7561f),
                    ExitPosition = new Vector3(163.1478f, 161.594f, -617.2457f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "STR_MetalDoorExt_E_Left_Prefab (1)",
                    DoorTransformPosition = new Vector3(135.5295f, 163.0083f, -665.3468f),
                    EntryPosition = new Vector3(135.3641f, 163.3346f, -664.526f),
                    ExitPosition = new Vector3(134.8359f, 161.8245f, -665.366f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "STR_MetalDoorExt_E_Left_Prefab (2)",
                    DoorTransformPosition = new Vector3(178.2659f, 168.077f, -662.2448f),
                    EntryPosition = new Vector3(177.4385f, 168.3956f, -662.3871f),
                    ExitPosition = new Vector3(179.0541f, 166.8872f, -662.8561f)

                    },
                },
            },
            // 2. AirfieldTrailerB (SIConfigTool 2026-10-04: 1 door(s), lift 0.85 m)
            new InteriorConfig
            {
                InstanceId = "AirfieldTrailerB_1",
                ExteriorSceneName = "AirfieldRegion",
                InteriorSceneBaseName = "AirfieldTrailerB",
                ExteriorShellPrefabName = "STRSPAWN_CampTrailerB_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(41.5055f, 161.3119f, -294.5734f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear", "FX_LightShaft_B", "WindowLightGroup", "Day Lights" },
                ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                RotationOffset = new Vector3(0f, 126.03f, 0f),
                // review by hand: DLM_AirfieldTrailerB_LGT_Prefab
            },

            // 3. AirfieldWoodCabinA (SIConfigTool 2026-10-04: 1 door(s), lift 0.50 m)
            new InteriorConfig
            {
                InstanceId = "AirfieldWoodCabinA_1",
                ExteriorSceneName = "AirfieldRegion",
                InteriorSceneBaseName = "AirfieldWoodCabinA",
                ExteriorShellPrefabName = "STR_WoodCabinE_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1011.914f, 288.8492f, -951.6462f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear", "FX_LightShaft_E", "FX_LightShaft_B" },
                ObjectsToDisable = new List<string> { "STR_WoodCabinInteriorAGlow_Prefab" },
                RotationOffset = new Vector3(0f, 73.12f, 0f),
            },

            // 4. TrailerE (SIConfigTool 2026-10-04: 1 door(s), lift 0.40 m)
            new InteriorConfig
            {
                InstanceId = "TrailerE_1",
                ExteriorSceneName = "AirfieldRegion",
                InteriorSceneBaseName = "TrailerE",
                ExteriorShellPrefabName = "STRSPAWN_CampTrailerE_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(-1082.437f, 235.4914f, -1064.176f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "InteriorLightingManager_Prefab", "FX_LightShaft_B", "FX_LightShaft_E", "CONTAINER_InaccessibleGear", "Daylight", "WindowLightGroup" },
                ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                RotationOffset = new Vector3(0f, 50.21f, 0f),
            },


            // MILTON

            // 1. GreyMother's House
            new InteriorConfig
            {
                 ExteriorSceneName = "MountainTownRegion",
                 InteriorSceneBaseName = "GreyMothersHouseA",
                 ExteriorShellPrefabName = "STR_GreyMothersHouse_Prefab",
                 YOffset = 0f,
                 ScaleAdjustment = new Vector3(1f, 1f, 1f),
                 FallbackPosition = new Vector3(1107.28f, 271.2501f, 1785.27f),
                 ForceExactPosition = true,
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear" },
                 ObjectsToDisable = new List<string> { "STR_GreyMothersHouseInteriorGlow_D_Prefab", "STR_GreyMothersHouseInteriorGlow_C_Prefab", "STR_GreyMothersHouseInteriorGlow_B_Prefab", "STR_GreyMothersHouseInteriorGlow_A_Prefab" },
                 EntrySpawnPosition = new Vector3(1106.251f, 272.4972f, 1783.099f),
                 ExitSpawnPosition = new Vector3(1104.889f, 269.7428f, 1783.645f),
                 RotationOffset = new Vector3(0f, 0f, 0f),
            },
            // 2. BankA (SIConfigTool 2026-10-04: 1 door(s), lift 1.20 m, CHECK ROTATION IN GAME)
            new InteriorConfig
            {
                InstanceId = "BankA_1",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "BankA",
                ExteriorShellPrefabName = "STR_BankA_Prefab (1)",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1070.222f, 262.3881f, 1734.072f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear", "FX_LightShaft_B", "FX_LightShaft_B (1)", "FX_LightShaft_B (2)", "Daytime", "Midday", "MiltonBank_Lighting_Prefab" },
                ObjectsToDisable = new List<string> { "STR_BankAGlowMesh_Prefab" },
                RotationOffset = new Vector3(0f, 336.26f, 0f),
            },

            // 3. ChurchB (SIConfigTool 2026-10-04: 1 door(s), lift 2.10 m, CHECK ROTATION IN GAME)
            new InteriorConfig
            {
                InstanceId = "ChurchB_1",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "ChurchB",
                ExteriorShellPrefabName = "STR_ChurchB_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(689.8438f, 289.5505f, 2102.106f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "InteriorLightingManager_Prefab", "FX_LightShaft_C", "CONTAINER_InaccessibleGear", "Daytime", "FX_LightShaft_B", "Midday", "FX_LightShaft_E" },
                ObjectsToDisable = new List<string> { "STR_ChurchBGlowMesh_Prefab" },
                RotationOffset = new Vector3(0f, 0f, 0f),
            },

            // 4. FarmHouseB (SIConfigTool 2026-10-04: 1 door(s), lift 0.90 m, CHECK ROTATION IN GAME)
            new InteriorConfig
            {
                InstanceId = "FarmHouseB_1",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "FarmHouseB",
                ExteriorShellPrefabName = "STR_FarmHouseB_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(551.5956f, 223.5412f, 1535.041f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "CONTAINER_InaccessibleGear", "FarmHouseB_Lighting_Prefab", "Midday", "Daytime" },
                ObjectsToDisable = new List<string> { "STR_FarmHouseWindowGlowA", "STR_FarmHouseWindowGlowB" },
                RotationOffset = new Vector3(0f, 308.02f, 0f),
            },

            // 5. MiltonHouseA (SIConfigTool 2026-10-04: 1 door(s), lift 0.40 m)
            new InteriorConfig
            {
                InstanceId = "MiltonHouseA_1",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "MiltonHouseA",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorAMilton_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1132.05f, 262.5757f, 1608.086f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "MiltonHouseA_Lighting_Prefab", "CONTAINER_InaccessibleGear" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseAWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 174.5f, 0f),
            },

            // 6. MiltonHouseA (SIConfigTool 2026-10-04: 1 door(s), lift 2.85 m)
            new InteriorConfig
            {
                InstanceId = "MiltonHouseA_2",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "MiltonHouseA",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorAMilton_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1170.884f, 265.4258f, 1579.221f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "MiltonHouseA_Lighting_Prefab", "CONTAINER_InaccessibleGear" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseAWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 73.85f, 0f),
            },

            // 7. MiltonHouseA (SIConfigTool 2026-10-04: 1 door(s), lift 0.60 m)
            new InteriorConfig
            {
                InstanceId = "MiltonHouseA_3",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "MiltonHouseA",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorAMilton_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1177.038f, 262.4277f, 1657.273f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "MiltonHouseA_Lighting_Prefab", "CONTAINER_InaccessibleGear" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseAWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 338.19f, 0f),
            },

            // 8. MiltonHouseC (SIConfigTool 2026-10-04: 2 door(s), lift 1.55 m)
            // not generated: HouseBasementC
            new InteriorConfig
            {
                InstanceId = "MiltonHouseC_1",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "MiltonHouseC",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorCMilton_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1032.927f, 260.8692f, 1675.15f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear", "FX_LightShaft_B", "FX_LightShaft_E", "Midday", "MiltonHouseC_Lighting_Prefab", "Daytime" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseCWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 159.35f, 0f),
            },

            // 9. MiltonHouseC (SIConfigTool 2026-10-04: 2 door(s), lift 1.45 m)
            // not generated: HouseBasementC
            new InteriorConfig
            {
                InstanceId = "MiltonHouseC_2",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "MiltonHouseC",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorCMilton_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1130.294f, 263.2492f, 1589.577f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear", "FX_LightShaft_B", "FX_LightShaft_E", "Midday", "MiltonHouseC_Lighting_Prefab", "Daytime" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseCWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 166.74f, 0f),
            },

            // 10. MiltonHouseC (SIConfigTool 2026-10-04: 2 door(s), lift 1.85 m)
            // not generated: HouseBasementC
            new InteriorConfig
            {
                InstanceId = "MiltonHouseC_3",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "MiltonHouseC",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorCMilton_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1186.483f, 262.4392f, 1621.075f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear", "FX_LightShaft_B", "FX_LightShaft_E", "Midday", "MiltonHouseC_Lighting_Prefab", "Daytime" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseCWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 74.09f, 0f),
            },

            // 11. MiltonHouseD (SIConfigTool 2026-10-04: 2 door(s), lift 0.95 m)
            new InteriorConfig
            {
                InstanceId = "MiltonHouseD_1",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "MiltonHouseD",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorDMilton_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1097.728f, 262.3578f, 1692.206f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear", "FX_LightShaft_B", "FX_LightShaft_E", "Daytime", "MiltonHouseD_Lighting_Prefab", "Midday" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseDWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 62.09f, 0f),
            },

            // 12. MiltonHouseD (SIConfigTool 2026-10-04: 2 door(s), lift 1.20 m)
            new InteriorConfig
            {
                InstanceId = "MiltonHouseD_2",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "MiltonHouseD",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorDMilton_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1119.692f, 262.6279f, 1643.049f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear", "FX_LightShaft_B", "FX_LightShaft_E", "Daytime", "MiltonHouseD_Lighting_Prefab", "Midday" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseDWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 156.98f, 0f),
            },

            // 13. MiltonHouseD (SIConfigTool 2026-10-04: 2 door(s), lift 2.85 m)
            new InteriorConfig
            {
                InstanceId = "MiltonHouseD_3",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "MiltonHouseD",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorDMilton_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1151.468f, 267.0078f, 1763.601f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear", "FX_LightShaft_B", "FX_LightShaft_E", "Daytime", "MiltonHouseD_Lighting_Prefab", "Midday" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseDWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 345.94f, 0f),
            },

            // 14. MiltonHouseF1 (SIConfigTool 2026-10-04: 1 door(s), lift 3.35 m)
            new InteriorConfig
            {
                InstanceId = "MiltonHouseF1_1",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "MiltonHouseF1",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorFMilton_Prefab_1",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1021.75f, 261.9876f, 1669.93f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "Midday", "CONTAINER_InaccessibleGear", "FX_LightShaft_B", "Daytime", "MiltonHouseF1_Lighting_Prefab", "FX_LightShaft_E" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseFWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 257.54f, 0f),
            },

            // 15. MiltonHouseF1 (SIConfigTool 2026-10-04: 1 door(s), lift 1.30 m)
            new InteriorConfig
            {
                InstanceId = "MiltonHouseF1_2",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "MiltonHouseF1",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorFMilton_Prefab_1",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1025.461f, 262.5942f, 1717.182f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "Midday", "CONTAINER_InaccessibleGear", "FX_LightShaft_B", "Daytime", "MiltonHouseF1_Lighting_Prefab", "FX_LightShaft_E" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseFWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 76.27f, 0f),
            },

            // 16. MiltonHouseF1 (SIConfigTool 2026-10-04: 1 door(s), lift 1.95 m)
            new InteriorConfig
            {
                InstanceId = "MiltonHouseF1_3",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "MiltonHouseF1",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorFMilton_Prefab_1",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1153.097f, 262.7344f, 1647.814f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "Midday", "CONTAINER_InaccessibleGear", "FX_LightShaft_B", "Daytime", "MiltonHouseF1_Lighting_Prefab", "FX_LightShaft_E" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseFWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 157.67f, 0f),
            },

            // 17. MiltonHouseF2 (SIConfigTool 2026-10-04: 1 door(s), lift 1.00 m)
            // 2 variant groups
            new InteriorConfig
            {
                InstanceId = "MiltonHouseF2_1",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "MiltonHouseF2",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorFMilton_Prefab_2",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1171.951f, 262.2358f, 1671.403f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "Daytime", "MiltonHouseF2_Lighting_Prefab", "FX_LightShaft_E", "Midday" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseFWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 156.92f, 0f),
            },

            // 18. MiltonHouseF2 (SIConfigTool 2026-10-04: 1 door(s), lift 2.15 m)
            new InteriorConfig
            {
                InstanceId = "MiltonHouseF2_2",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "MiltonHouseF2",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorFMilton_Prefab_2",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1190.744f, 262.8642f, 1605.864f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "Daytime", "MiltonHouseF2_Lighting_Prefab", "FX_LightShaft_E", "Midday" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseFWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 249.8f, 0f),
            },

            // 19. MiltonHouseF3 (SIConfigTool 2026-10-04: 1 door(s), lift 1.00 m)
            new InteriorConfig
            {
                InstanceId = "MiltonHouseF3_1",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "MiltonHouseF3",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorFMilton_Prefab_3",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1128.52f, 264.5311f, 1756.531f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "MiltonHouseF3_Lighting_Prefab", "Daytime", "FX_LightShaft_E", "Midday" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseFWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 71.83f, 0f),
            },

            // 20. MiltonHouseH1 (SIConfigTool 2026-10-04: 2 door(s), lift 0.75 m)
            new InteriorConfig
            {
                InstanceId = "MiltonHouseH1_1",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "MiltonHouseH1",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorHMilton_Prefab (2)",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1040.116f, 264.3463f, 1794.546f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear", "FX_LightShaft_B", "Daytime", "FX_LightShaft_E", "Midday", "MiltonHouseH1_Lighting_Prefab" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseHWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 153.43f, 0f),
            },

            // 21. MiltonHouseH1 (SIConfigTool 2026-10-04: 2 door(s), lift 0.40 m)
            // 2 variant groups
            new InteriorConfig
            {
                InstanceId = "MiltonHouseH1_2",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "MiltonHouseH1",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorHMilton_Prefab (2)",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1101.413f, 261.8463f, 1682.539f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear", "FX_LightShaft_B", "Daytime", "FX_LightShaft_E", "Midday", "MiltonHouseH1_Lighting_Prefab" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseHWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 153.15f, 0f),
            },

            // 22. MiltonHouseH2 (SIConfigTool 2026-10-04: 2 door(s), lift 0.75 m)
            new InteriorConfig
            {
                InstanceId = "MiltonHouseH2_1",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "MiltonHouseH2",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorHMilton_Prefab (4)",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1040.116f, 264.3462f, 1794.546f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "MiltonHouseH2_Lighting_Prefab", "FX_LightShaft_E", "Daytime", "Midday" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseHWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 153.43f, 0f),
            },

            // 23. MiltonHouseH2 (SIConfigTool 2026-10-04: 2 door(s), lift 0.70 m)
            new InteriorConfig
            {
                InstanceId = "MiltonHouseH2_2",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "MiltonHouseH2",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorHMilton_Prefab (3)",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1121.228f, 263.888f, 1750.479f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "MiltonHouseH2_Lighting_Prefab", "FX_LightShaft_E", "Daytime", "Midday" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseHWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 152.44f, 0f),
            },

            // 24. MiltonHouseH2 (SIConfigTool 2026-10-04: 2 door(s), lift 1.45 m)
            new InteriorConfig
            {
                InstanceId = "MiltonHouseH2_3",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "MiltonHouseH2",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorHMilton_Prefab (3)",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1160.324f, 262.2263f, 1621.533f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "MiltonHouseH2_Lighting_Prefab", "FX_LightShaft_E", "Daytime", "Midday" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseHWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 251.51f, 0f),
            },

            // 25. MiltonHouseH3 (SIConfigTool 2026-10-04: 2 door(s), lift 0.95 m)
            new InteriorConfig
            {
                InstanceId = "MiltonHouseH3_1",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "MiltonHouseH3",
                ExteriorShellPrefabName = "STRSPAWN_HouseExteriorHMilton_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(989.0227f, 281.7963f, 1552.099f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "Midday", "FX_LightShaft_E", "Daytime", "MiltonHouseH3_Lighting_Prefab" },
                ObjectsToDisable = new List<string> { "STR_CoastalHouseHWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 120.49f, 0f),
            },

            // 26. PostOfficeA (SIConfigTool 2026-10-04: 1 door(s), lift 0.35 m)
            new InteriorConfig
            {
                InstanceId = "PostOfficeA_1",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "PostOfficeA",
                ExteriorShellPrefabName = "STR_PostOfficeA_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1128.461f, 263.76f, 1717.348f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear", "FX_LightShaft_B" },
                ObjectsToDisable = new List<string> { "STR_PostOfficeMainRoomGlowMesh_Prefab" },
                RotationOffset = new Vector3(0f, 69.67f, 0f),
                // review by hand: DarkLightingManager_Prefab
            },
            // 27. MiltonTrailerB (SIConfigTool 2026-10-04: 1 door(s), lift 0.20 m)
            new InteriorConfig
            {
                InstanceId = "MiltonTrailerB_1",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "MiltonTrailerB",
                ExteriorShellPrefabName = "STRSPAWN_CampTrailerB_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(995.3348f, 358.1516f, 2294.262f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "InteriorLightingManager_Prefab", "FX_LightShaft_B", "CONTAINER_InaccessibleGear", "FX_LightShaft_E", "Daylight", "WindowLightGroup", "Midday" },
                ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                RotationOffset = new Vector3(0f, 36.53f, 0f),
            },
            // 28. ConvenienceStoreA (SIConfigTool 2026-10-04: 2 door(s), lift 2.30 m)
            new InteriorConfig
            {
                InstanceId = "ConvenienceStoreA_1",
                ExteriorSceneName = "MountainTownRegion",
                InteriorSceneBaseName = "ConvenienceStoreA",
                ExteriorShellPrefabName = "STR_ConvenienceStoreA_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1174.88f, 268.793f, 1352.258f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "CONTAINER_InaccessibleGear", "Daytime", "FX_LightShaft_E", "Midday", "MiltonConvenienceStoreA_Lighting_Prefab" },
                ObjectsToDisable = new List<string> { "STR_ConvenienceStoreAGlowMesh_Prefab" },
                RotationOffset = new Vector3(0f, 92.57f, 0f),
            },




            //DESOLATION POINT:

            // 1. LighthouseA (SIConfigTool 2026-10-04: 2 door(s), lift 0.00 m, CHECK ROTATION IN GAME)
            new InteriorConfig
            {
                InstanceId = "LighthouseA_1",
                ExteriorSceneName = "WhalingStationRegion",
                InteriorSceneBaseName = "LighthouseA",
                ExteriorShellPrefabName = "STRSPAWN_LighthouseExt_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(727.8728f, 47.5505f, 764.2289f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "Daylight", "FX_LightShaft_D", "Midday", "CONTAINER_InaccessibleGear", "FX_LightShaft_B", "FX_LightShaft_E", "Daytime", "LighthouseA_Lighting_Prefab" },
                ObjectsToDisable = new List<string> { "STR_LighthouseGlowPane_Prefab" },
                RotationOffset = new Vector3(0f, 41.63f, 0f),
            },

            // 2. TrailerB (SIConfigTool 2026-10-04: 1 door(s), lift 0.70 m)
            new InteriorConfig
            {
                InstanceId = "CampTrailerB_3",
                ExteriorSceneName = "WhalingStationRegion",
                InteriorSceneBaseName = "TrailerB",
                ExteriorShellPrefabName = "STRSPAWN_CampTrailerF_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1022.391f, 20.2226f, 1227.002f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                RotationOffset = new Vector3(0f, 270f, 0f),
            },

            // 3. TrailerD (SIConfigTool 2026-10-04: 1 door(s), lift 1.65 m)
            new InteriorConfig
            {
                InstanceId = "CampTrailerD_2",
                ExteriorSceneName = "WhalingStationRegion",
                InteriorSceneBaseName = "TrailerD",
                ExteriorShellPrefabName = "STRSPAWN_CampTrailerD_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1007.685f, 21.5237f, 1330.214f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                RotationOffset = new Vector3(0f, 150.37f, 0f),
            },
            // 4. WhalingShipA (SIConfigTool 2026-10-04: 2 door(s), lift 1.90 m, CHECK ROTATION IN GAME)
            new InteriorConfig
            {
                InstanceId = "WhalingShipA_1",
                ExteriorSceneName = "WhalingStationRegion",
                InteriorSceneBaseName = "WhalingShipA",
                ExteriorShellPrefabName = "STR_WhalingShipA_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1029.473f, 20.5504f, 1001.735f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "InteriorLightingManager_Prefab", "Daylight", "CONTAINER_InaccessibleGear", "Midday" },
                ObjectsToDisable = new List<string> { "STR_WhalingShipGlow_Prefab " },
                RotationOffset = new Vector3(0f, 258.89f, 0f),
            },

            // 5. WhalingWarehouseA (SIConfigTool 2026-10-04: 2 door(s), lift 2.50 m, CHECK ROTATION IN GAME)
            new InteriorConfig
            {
                InstanceId = "WhalingWarehouseA_1",
                ExteriorSceneName = "WhalingStationRegion",
                InteriorSceneBaseName = "WhalingWarehouseA",
                ExteriorShellPrefabName = "STR_WarehouseA_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1039.082f, 21.2003f, 1281.206f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "InteriorLightingManager_Prefab", "Daylight", "Midday", "CONTAINER_InaccessibleGear" },
                ObjectsToDisable = new List<string> { "STR_WarehouseWindowsA_Prefab" },
                RotationOffset = new Vector3(0f, 90f, 0f),
            },
            // 6. TrailerSShape (SIConfigTool 2026-10-04: 3 door(s), lift 1.40 m)
            new InteriorConfig
            {
                InstanceId = "TrailerSShape_1",
                ExteriorSceneName = "WhalingStationRegion",
                InteriorSceneBaseName = "TrailerSShape",
                ExteriorShellPrefabName = "STRSPAWN_CampTrailerD_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(1023.858f, 20.8651f, 1228.719f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "InteriorLightingManager_Prefab", "FX_LightShaft_B", "FX_LightShaft_C", "FX_LightShaft_E", "CONTAINER_InaccessibleGear", "Daytime", "Midday", "Daylight" },
                ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                RotationOffset = new Vector3(0f, 180f, 0f),
            },




            //BLEAK INLET:

            // 1. CanneryTrailerA (SIConfigTool 2026-10-04: 1 door(s), lift 0.90 m)
            new InteriorConfig
            {
                InstanceId = "CanneryTrailerA_1",
                ExteriorSceneName = "CanneryRegion",
                InteriorSceneBaseName = "CanneryTrailerA",
                ExteriorShellPrefabName = "STRSPAWN_CampTrailerD_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(-471.7725f, 32.9736f, -501.8221f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "InteriorLightingManager_Prefab", "FX_LightShaft_E", "FX_LightShaft_B", "CONTAINER_InaccessibleGear", "WindowLightGroup", "Daylight" },
                ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                RotationOffset = new Vector3(0f, 79.67f, 0f),
            },

            // 2. RadioControlHutB (SIConfigTool 2026-10-04: 1 door(s), lift 1.35 m)
            new InteriorConfig
            {
                InstanceId = "RadioControlHutB_1",
                ExteriorSceneName = "CanneryRegion",
                InteriorSceneBaseName = "RadioControlHutB",
                ExteriorShellPrefabName = "STR_RadioControlHutBaseB_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(-70.1429f, 139.044f, 20.0171f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear", "Day Lights", "Glow_Plane (1)", "Glow_Plane", "Glow_Plane (2)" },
                ObjectsToDisable = new List<string> { "STR_RadioControlHutInteriorWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 259.38f, 0f),
                // review by hand: DLM_RadioControlHutB_SURVIVAL_LGT_Prefab
            },

            // 3. TrailerB (SIConfigTool 2026-10-04: 1 door(s), lift 0.00 m)
            new InteriorConfig
            {
                InstanceId = "CampTrailerB_4",
                ExteriorSceneName = "CanneryRegion",
                InteriorSceneBaseName = "TrailerB",
                ExteriorShellPrefabName = "STRSPAWN_CampTrailerB_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(-760.3819f, 36.5126f, -398.0035f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                RotationOffset = new Vector3(0f, 85.95f, 0f),
            },

            // 4. TrailerC (SIConfigTool 2026-10-04: 1 door(s), lift 0.55 m)
            new InteriorConfig
            {
                InstanceId = "CampTrailerC_4",
                ExteriorSceneName = "CanneryRegion",
                InteriorSceneBaseName = "TrailerC",
                ExteriorShellPrefabName = "STRSPAWN_CampTrailerC_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(-746.5468f, 37.5804f, -388.3692f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                RotationOffset = new Vector3(0f, 184.41f, 0f),
            },

            // 5. TrailerD (SIConfigTool 2026-10-04: 1 door(s), lift 0.90 m)
            new InteriorConfig
            {
                InstanceId = "CampTrailerD_3",
                ExteriorSceneName = "CanneryRegion",
                InteriorSceneBaseName = "TrailerD",
                ExteriorShellPrefabName = "STRSPAWN_CampTrailerE_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(-642.2117f, 36.4839f, -74.8461f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                RotationOffset = new Vector3(0f, 152.95f, 0f),
            },

            // 7. WoodCabinA (SIConfigTool 2026-10-04: 1 door(s), lift 0.00 m)
            new InteriorConfig
            {
                InstanceId = "WoodCabinA_1",
                ExteriorSceneName = "CanneryRegion",
                InteriorSceneBaseName = "WoodCabinA",
                ExteriorShellPrefabName = "STR_WoodCabinA_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(170.3212f, 30.5899f, -431.82f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear", "FX_LightShaft_E", "FX_LightShaft_B" },
                ObjectsToDisable = new List<string> { "STR_WoodCabinInteriorAGlow_Prefab" },
                RotationOffset = new Vector3(0f, 349.35f, 0f),
            },

            // 8. WoodCabinC (SIConfigTool 2026-10-04: 1 door(s), lift 0.40 m)
            new InteriorConfig
            {
                InstanceId = "WoodCabinC_1",
                ExteriorSceneName = "CanneryRegion",
                InteriorSceneBaseName = "WoodCabinC",
                ExteriorShellPrefabName = "STR_WoodCabinC_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(677.4034f, 106.1889f, 423.9478f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_E", "FX_LightShaft_B", "CONTAINER_InaccessibleGear" },
                ObjectsToDisable = new List<string> { "STR_WoodCabinInteriorCGlow_Prefab" },
                RotationOffset = new Vector3(0f, 141.21f, 0f),
            },
            // 8. MaintenanceShedB (SIConfigTool 2026-10-04: 1 door(s), lift 0.00 m, CHECK ROTATION IN GAME)
            new InteriorConfig
            {
                InstanceId = "MaintenanceShedB_1",
                ExteriorSceneName = "CanneryRegion",
                InteriorSceneBaseName = "MaintenanceShedB",
                ExteriorShellPrefabName = "STR_MaintenanceShedB_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(-377.1116f, 32.2234f, -609.7639f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "InteriorLightingManager_Prefab", "FX_LightShaft_E (2)", "FX_LightShaft_E (1)", "CONTAINER_InaccessibleGear", "FX_LightShaft_E", "FX_LightShaft_B" },
                ObjectsToDisable = new List<string> { "STR_MaintenanceShedBGlow_Prefab" },
                RotationOffset = new Vector3(0f, 44.91f, 0f),
            },



            //BROKEN RAILROAD:

            // 1. HuntingLodgeA (SIConfigTool 2026-10-04: 4 door(s), lift 0.55 m, CHECK ROTATION IN GAME)
            new InteriorConfig
            {
                InstanceId = "HuntingLodgeA_1",
                ExteriorSceneName = "TracksRegion",
                InteriorSceneBaseName = "HuntingLodgeA",
                ExteriorShellPrefabName = "STR_HuntingLodgeA_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(63.9663f, 246.1678f, 462.9071f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "CONTAINER_InaccessibleGear", "FX_LightShaft_E", "HuntingLodgeA_Lighting_Prefab", "WindowLightGroup", "Midday", "Daytime" },
                ObjectsToDisable = new List<string> { "STR_HuntingLodgeAIntBlockout_Glow_Prefab" },
                RotationOffset = new Vector3(0f, 118.11f, 0f),
                TerrainHoleSize = new Vector3(24.27f, 10f, 14.467f), // basement footprint in the building's frame
                TerrainHoleOffset = new Vector3(64.409f, 240.571f, 463.59f), // centre; y = sink depth
                TerrainHoleYaw = 118.11f,
            },

            // 2. MaintenanceShedA (SIConfigTool 2026-10-04: 3 door(s), lift 2.15 m, CHECK ROTATION IN GAME)
            new InteriorConfig
            {
                InstanceId = "MaintenanceShedA_1",
                ExteriorSceneName = "TracksRegion",
                InteriorSceneBaseName = "MaintenanceShedA",
                ExteriorShellPrefabName = "STR_MaintenanceShedA_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(585.0338f, 201.2171f, 545.0409f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "FX_LightShaft_E", "FX_LightShaft_B", "WindowLightGroup", "Daylight" },
                ObjectsToDisable = new List<string> { "STR_MaintenanceShedAGlow_Prefab" },
                RotationOffset = new Vector3(0f, 83.59f, 0f),
            },


            //ASH CANYON:

            // 1. AshCabinD (SIConfigTool 2026-10-04: 1 door(s), lift 0.70 m)
            new InteriorConfig
            {
                InstanceId = "AshCabinD_1",
                ExteriorSceneName = "AshCanyonRegion",
                InteriorSceneBaseName = "AshCabinD",
                ExteriorShellPrefabName = "STRSPAWN_LakeCabinG_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(351.6365f, 235.2423f, 573.3148f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear", "FX_LightShaft_E", "FX_LightShaft_B" },
                ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                RotationOffset = new Vector3(0f, 35.33f, 0f),
            },

            // 2. AshCabinF (SIConfigTool 2026-10-04: 1 door(s), lift 1.10 m)
            new InteriorConfig
            {
                InstanceId = "AshCabinF_1",
                ExteriorSceneName = "AshCanyonRegion",
                InteriorSceneBaseName = "AshCabinF",
                ExteriorShellPrefabName = "STRSPAWN_LakeCabinF_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(54.2323f, 73.3767f, 178.8892f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "Daylight", "FX_LightShaft_E", "CONTAINER_InaccessibleGear", "WindowLightGroup" },
                ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                RotationOffset = new Vector3(0f, 267.32f, 0f),
                // review by hand: DarkLightingManager_Prefab
            },

            // 3. AshWoodCabinA (SIConfigTool 2026-10-04: 1 door(s), lift 2.20 m)
            new InteriorConfig
            {
                InstanceId = "AshWoodCabinA_1",
                ExteriorSceneName = "AshCanyonRegion",
                InteriorSceneBaseName = "AshWoodCabinA",
                ExteriorShellPrefabName = "STR_WoodCabinE_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(735.5153f, 93.1299f, -313.2222f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear", "FX_LightShaft_E", "FX_LightShaft_B" },
                ObjectsToDisable = new List<string> { "STR_WoodCabinInteriorAGlow_Prefab" },
                RotationOffset = new Vector3(0f, 39.76f, 0f),
            },

            // 4. WoodCabinB (SIConfigTool 2026-10-04: 1 door(s), lift 0.60 m)
            new InteriorConfig
            {
                InstanceId = "WoodCabinB_1",
                ExteriorSceneName = "AshCanyonRegion",
                InteriorSceneBaseName = "WoodCabinB",
                ExteriorShellPrefabName = "STR_WoodCabinD_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(229.1493f, 267.6943f, 415.2916f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "CONTAINER_InaccessibleGear", "FX_LightShaft_E" },
                ObjectsToDisable = new List<string> { "STR_WoodCabinInteriorBGlow_Prefab" },
                RotationOffset = new Vector3(0f, 35.48f, 0f),
            },


            //BLACKROCK:

            // 1. BlackrockPowerplantA (SIConfigTool 2026-10-04: 2 door(s), lift 0.00 m, CHECK ROTATION IN GAME)
            new InteriorConfig
            {
                InstanceId = "BlackrockPowerplantA_1",
                ExteriorSceneName = "BlackrockRegion",
                InteriorSceneBaseName = "BlackrockPowerplantA",
                ExteriorShellPrefabName = "STR_Powerplant_Ext_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(-366.867f, 76.2223f, 440.1278f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear", "FX_LightShaft_E" },
                ObjectsToDisable = new List<string> { "STR_Powerplant_IntWindows_Prefab", "STR_Powerplant_IntWindowsMatte_Prefab"},
                RotationOffset = new Vector3(0f, 270.68f, 0f),
                // review by hand: DLM_BlackrockPowerplantA_LGT_Prefab
            },

            // 2. RadioControlHutC (SIConfigTool 2026-10-04: 1 door(s), lift 1.35 m)
            new InteriorConfig
            {
                InstanceId = "RadioControlHutC_1",
                ExteriorSceneName = "BlackrockRegion",
                InteriorSceneBaseName = "RadioControlHutC",
                ExteriorShellPrefabName = "STR_RadioControlHutBase_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(-582.2979f, 107.3815f, -473.7305f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear", "Day Lights" },
                ObjectsToDisable = new List<string> { "STR_RadioControlHutInteriorWindowGlow_Prefab" },
                RotationOffset = new Vector3(0f, 25.72f, 0f),
                // review by hand: DLM_RadioControlHutC_SURVIVAL_LGT_Prefab
            },

            // 3. TrailerB (SIConfigTool 2026-10-04: 1 door(s), lift 0.80 m)
            // not generated: BlackRockTrailerB
            new InteriorConfig
            {
                InstanceId = "CampTrailerB_5",
                ExteriorSceneName = "BlackrockRegion",
                InteriorSceneBaseName = "TrailerB",
                ExteriorShellPrefabName = "STRSPAWN_CampTrailerB_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(-657.8255f, 105.8125f, -27.5393f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                RotationOffset = new Vector3(0f, 280.37f, 0f),
            },

            // 4. TrailerD (SIConfigTool 2026-10-04: 1 door(s), lift 1.00 m)
            new InteriorConfig
            {
                InstanceId = "CampTrailerD_4",
                ExteriorSceneName = "BlackrockRegion",
                InteriorSceneBaseName = "TrailerD",
                ExteriorShellPrefabName = "STR_CampTrailerDBase_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(450.2777f, 211.2955f, -223.1138f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                RotationOffset = new Vector3(0f, 111.04f, 0f),
            },


            //SUNDERED PASS:

            // 1. MountainPassBasement (SIConfigTool 2026-10-04: 1 door(s), underground: no lift, terrain hole instead)
            new InteriorConfig
            {
                InstanceId = "MountainPassBasement_1",
                ExteriorSceneName = "MountainPassRegion",
                InteriorSceneBaseName = "MountainPassBasement",
                ExteriorShellPrefabName = "STR_MountainPassBasementFoundation_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(-160.6627f, 440.572f, -430.0739f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear" },
                ObjectsToDisable = new List<string> { },
                RotationOffset = new Vector3(0f, 3.12f, 0f),
                TerrainHoleSize = new Vector3(15.91f, 10f, 21.852f), // room footprint in the building's frame
                TerrainHoleOffset = new Vector3(-158.9f, 439.113f, -425.303f), // centre; y = sink depth
                TerrainHoleYaw = 3.12f,
            },

            // 2. MountainPassCabinA (SIConfigTool 2026-10-04: 1 door(s), lift 0.00 m)
            new InteriorConfig
            {
                InstanceId = "MountainPassCabinA_1",
                ExteriorSceneName = "MountainPassRegion",
                InteriorSceneBaseName = "MountainPassCabinA",
                ExteriorShellPrefabName = "STR_MountainPassCabinA_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(497.6086f, 101.138f, 649.1263f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "CONTAINER_InaccessibleGear" },
                ObjectsToDisable = new List<string> { "STR_WoodCabinInteriorBGlow_Prefab" },
                RotationOffset = new Vector3(0f, 68.54f, 0f),
            },

            // 3. RadarBuilding (SIConfigTool 2026-10-04: 1 door(s), lift 0.40 m)
            new InteriorConfig
            {
                InstanceId = "RadarBuilding_1",
                ExteriorSceneName = "MountainPassRegion",
                InteriorSceneBaseName = "RadarBuilding",
                ExteriorShellPrefabName = "STR_RadarStation_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(536.8404f, 596.7684f, -620.959f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear" },
                ObjectsToDisable = new List<string> { "OBJ_GenericWindowGlow_Prefab (1)", "OBJ_GenericWindowGlow_Prefab (3)", "OBJ_GenericWindowGlow_Prefab", "OBJ_GenericWindowGlow_Prefab (2)", "OBJ_GenericWindowGlow_Prefab (4)", "STR_RadarStation_InteriorWindowGlow" },
                RotationOffset = new Vector3(0f, 85.76f, 0f),
                // review by hand: DLM_RadarBuilding_Light_Prefab
            },

            //MININGREGION:

            // 1. MineConcentratorBuilding (SIConfigTool 2026-10-04: 7 door(s), no lift: lower floors sit in the hillside, CHECK ROTATION IN GAME)
            new InteriorConfig
            {
                InstanceId = "MineConcentratorBuilding_1",
                ExteriorSceneName = "MiningRegion",
                InteriorSceneBaseName = "MineConcentratorBuilding",
                ExteriorShellPrefabName = "STR_ConveyorBuilding_Ext_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(-258.5752f, 199.4149f, 140.2395f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear", "Day Lights" },
                ObjectsToDisable = new List<string> { "OBJ_GenericWindowGlow_Prefab (9)", "OBJ_GenericWindowGlow_Prefab", "OBJ_GenericWindowGlow_Prefab (8)", "OBJ_GenericWindowGlow_Prefab (7)", "OBJ_GenericWindowGlow_Prefab (5)", "OBJ_GenericWindowGlow_Prefab (13)", "OBJ_GenericWindowGlow_Prefab (11)", "OBJ_GenericWindowGlow_Prefab (16)", "OBJ_GenericWindowGlow_Prefab (14)", "OBJ_GenericWindowGlow_Prefab (6)", "OBJ_GenericWindowGlow_Prefab (15)", "OBJ_GenericWindowGlow_Prefab (3)", "OBJ_GenericWindowGlow_Prefab (1)", "OBJ_GenericWindowGlow_Prefab (12)", "OBJ_GenericWindowGlow_Prefab (4)", "OBJ_GenericWindowGlow_Prefab (2)", "OBJ_GenericWindowGlow_Prefab (10)" },
                RotationOffset = new Vector3(0f, 306.73f, 0f),
                BoundsRadius = 146f, // the interior reaches 141 m from its root
                // the parts dug into the hill: snow showed inside them and under the elevator
                TerrainHoles = new List<TerrainHoleRect>
                {
                    new TerrainHoleRect(new Vector3(-249.468f, 181.9f, 141.242f), new Vector3(70.5f, 10f, 34.6f), 306.73f),   // main hall
                    new TerrainHoleRect(new Vector3(-254.076f, 169.0f, 160.901f), new Vector3(71.5f, 10f, 14.5f), 306.73f),   // conveyor tunnel
                    new TerrainHoleRect(new Vector3(-213.229f, 192.9f, 203.100f), new Vector3(17.0f, 10f, 27.5f), 306.73f),   // conveyor building
                    new TerrainHoleRect(new Vector3(-185.731f, 199.5f, 244.968f), new Vector3(30.0f, 10f, 40.5f), 306.73f),   // hoist room
                    new TerrainHoleRect(new Vector3(-186.057f, 128.0f, 255.817f), new Vector3(24.0f, 10f, 16.0f), 306.73f),   // elevator shaft and mine levels
                    new TerrainHoleRect(new Vector3(-307.770f, 228.4f, 178.819f), new Vector3(23.0f, 10f, 7.0f), 306.73f),   // upper floor
                },
                // review by hand: DLM_MineConcentrator_SURVIVAL_LGT_Prefab
            },

            // 2. PumpHouse (SIConfigTool 2026-10-04: 2 door(s), lift 1.65 m, CHECK ROTATION IN GAME)
            new InteriorConfig
            {
                InstanceId = "PumpHouse_1",
                ExteriorSceneName = "MiningRegion",
                InteriorSceneBaseName = "PumpHouse",
                ExteriorShellPrefabName = "STR_PumpHouse_Ext_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(-36.19f, 189.743f, 4.6002f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear", "Day Lights", "Glow_Plane (1)", "Glow_Plane", "Glow_Plane (2)" },
                ObjectsToDisable = new List<string> { "PumpHouse_InteriorWindowGlow" },
                RotationOffset = new Vector3(0f, 12.31f, 0f),
                // review by hand: DLM_PumpHouse_SURVIVAL_LGT_Prefab, AMB_sun
            },

            // 3. TrailerD (SIConfigTool 2026-10-04: 1 door(s), lift 0.30 m)
            new InteriorConfig
            {
                InstanceId = "CampTrailerD_5",
                ExteriorSceneName = "MiningRegion",
                InteriorSceneBaseName = "TrailerD",
                ExteriorShellPrefabName = "STRSPAWN_CampTrailerD_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1f, 1f, 1f),
                FallbackPosition = new Vector3(-340.7241f, 220.8907f, -456.9453f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLightGroup", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daylight" },
                ObjectsToDisable = new List<string> { "OBJ_TrailerWindow" },
                RotationOffset = new Vector3(0f, 35.7f, 0f),
            },

        };
    }
}
