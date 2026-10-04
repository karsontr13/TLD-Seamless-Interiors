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
                 EntrySpawnPosition = new Vector3(1478.075f, 21.0898f, -30.7528f),
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
                 EntrySpawnPosition = new Vector3(1632.244f, 38.18f, 1255.341f),
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
                 ObjectsToDestroy = new List<string> { "Beam Geometry", "Glow_Plane", "Glow_Plane (1)", "Glow_Plane (2)", "Day Lights", "CONTAINER_InaccessibleGear" },
                 ObjectsToDisable = new List<string> { "STR_RadioControlHutInteriorWindowGlow_Prefab" },
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
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "FX_LightShaft_C", "BarnHouseA_Lighting_Prefab", "CONTAINER_InaccessibleGear" },
                 ObjectsToDisable = new List<string> { "STR_BarnHouseAWindowsGlow_Prefab" },
                 EntrySpawnPosition = new Vector3(1675.586f, 45.17f, 1833.239f),
                 ExitSpawnPosition = new Vector3(1683.351f, 43.5867f, 1840.032f),
                 RotationOffset = new Vector3(0f, 325f, 0f),
                 DoorSpawnPoints = new List<DoorSpawnPoint>
                {

                    new DoorSpawnPoint {
                    DoorName = "BarnHouseAFrontEnterPoint",
                    DoorTransformPosition = new Vector3(1664.721f, 43.65f, 1845.217f),
                    EntryPosition = new Vector3(1664.802f, 45.17f, 1845.277f),
                    ExitPosition = new Vector3(1683.351f, 43.5867f, 1840.032f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "BarnHouseASideEnterPoint",
                    DoorTransformPosition = new Vector3(1675.579f, 43.82f, 1833.318f),
                    EntryPosition = new Vector3(1675.586f, 45.17f, 1833.239f),
                    ExitPosition = new Vector3(1677.822f, 43.5726f, 1827.994f)

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
                 ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "FX_LightShaft_E", "RuralStoreA_Lighting_Prefab", "CONTAINER_InaccessibleGear" },
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
                ObjectsToDisable = new List<string> { "STR_AF_Hangar_Interior_Main_Windows_Glow_Prefab", "STR_AF_Hangar_Interior_Side_Window_Glow_Prefab" },
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
        };
    }
}
