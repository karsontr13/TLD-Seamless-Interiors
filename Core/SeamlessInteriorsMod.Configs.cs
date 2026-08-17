using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        public static List<InteriorConfig> SupportedInteriors = new List<InteriorConfig>
        {
            // MYSTER LAKE:

            // 1. Camp Office
            new InteriorConfig
            {
                ExteriorSceneName = "LakeRegion",
                InteriorSceneBaseName = "CampOffice",
                ExteriorShellPrefabName = "STRSPAWN_CampOffice_Prefab",
                YOffset = 2f,
                ScaleAdjustment = new Vector3(1.05f, 0.98f, 1.05f),
                FallbackPosition = new Vector3(1019.738f, 28.7883f, 440.6331f),
                ForceExactPosition = false,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLight", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daytime" },
                ObjectsToDisable = new List<string> { "OBJ_LakeCabinInteriorWindow" },
                EntrySpawnPosition = new Vector3(),
                ExitSpawnPosition = new Vector3(),
                RotationOffset = Vector3.zero,
                DoorSpawnPoints = new List<DoorSpawnPoint>
                {
                    
                    new DoorSpawnPoint {
                    DoorName = "INTERACTIVE_CampOfficeInteriorDoorFront_Prefab",
                    DoorTransformPosition = new Vector3(1018.951f, 28.6588f, 445.1257f),
                    EntryPosition = new Vector3(1018.956f, 29.1809f, 444.5092f),
                    ExitPosition = new Vector3(1019.351f, 28.1647f, 444.2208f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "INTERACTIVE_CampOfficeInteriorDoorBack_Prefab",
                    DoorTransformPosition = new Vector3(1016.483f, 28.6588f, 435.9877f),
                    EntryPosition = new Vector3(1017.126f, 29.1593f, 436.104f),
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
                ScaleAdjustment = new Vector3(1.15f, 1f, 1.15f),
                FallbackPosition = new Vector3(1448.9f, 49.7f, 1027.8f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLight", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daytime", "Nighttime", "NightTime" },
                ObjectsToDisable = new List<string> { "STR_FarmHouseWindowGlowB", "STR_FarmHouseWindowGlowA", "STR_FarmHouseWindowGlow", "STR_FarmHouseParlourRoomWindowGlow_Prefab", "STR_FarmHouseWindowDoubleGlow" },
                EntrySpawnPosition = new Vector3(1452.784f, 51.5464f, 1033.838f),
                ExitSpawnPosition = new Vector3(1454.619f, 50.5074f, 1032.469f),
                RotationOffset = new Vector3(0f, 0f, 0f),
                DoorSpawnPoints = new List<DoorSpawnPoint>
                {
                    
                    new DoorSpawnPoint {
                    DoorName = "STR_FarmHouseDoor_Prefab",
                    DoorTransformPosition = new Vector3(1448.9f, 49.7f, 1020.762f),
                    EntryPosition = new Vector3(1448.936f, 51.5521f, 1021.433f),
                    ExitPosition = new Vector3(1447.433f, 50.5513f, 1020.128f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "STR_FarmHouseDoor_Prefab",
                    DoorTransformPosition = new Vector3(1444.637f, 49.7f, 1030.627f),
                    EntryPosition = new Vector3(1445.267f, 51.5521f, 1030.662f),
                    ExitPosition = new Vector3(1444.241f, 50.5513f, 1031.159f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "STR_FarmHouseDoor_Prefab",
                    DoorTransformPosition = new Vector3(1453.155f, 49.7f, 1033.919f),
                    EntryPosition = new Vector3(1452.784f, 51.5464f, 1033.838f),
                    ExitPosition = new Vector3(1454.619f, 50.5074f, 1032.469f)

                    },
                },
            },

            //COASTAL HİGHWAY:

            // 1. Quonset
            new InteriorConfig
            {
                ExteriorSceneName = "CoastalRegion",
                InteriorSceneBaseName = "QuonsetGasStation",
                ExteriorShellPrefabName = "STRSPAWN_QuonsetGasStation_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1.04f, 1f, 1.02f),
                FallbackPosition = new Vector3(772.6199f, 25.58f, 648.0303f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "FX_LightShaft_B", "WindowLight", "InteriorLightingManager_Prefab", "CONTAINER_InaccessibleGear", "Daytime" },
                ObjectsToDisable = new List<string> { "STR_GarageWindowsGlow_Prefab" },
                EntrySpawnPosition = new Vector3(761.7346f, 27.41f, 646.8323f),
                ExitSpawnPosition = new Vector3(761.3817f, 25.7838f, 646.5969f),
                RotationOffset = new Vector3(0f, 225f, 0f),
                DoorSpawnPoints = new List<DoorSpawnPoint>
                {
                    
                    new DoorSpawnPoint {
                    DoorName = "GasStationFrontEnterPoint",
                    DoorTransformPosition = new Vector3(762.0077f, 26.5441f, 647.0829f),
                    EntryPosition = new Vector3(761.7346f, 27.41f, 646.8323f),
                    ExitPosition = new Vector3(761.3817f, 25.7838f, 646.5969f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "GasStationBackEnterPoint",
                    DoorTransformPosition = new Vector3(775.7552f, 26.3226f, 655.2833f),
                    EntryPosition = new Vector3(776.0081f, 27.42f, 655.4044f),
                    ExitPosition = new Vector3(775.7437f, 25.7775f, 657.7051f)

                    },
                },
            },

            //FORSAKEN AİRFİLED:

            // 1. Hangar
            new InteriorConfig
            {
                ExteriorSceneName = "AirfieldRegion",
                InteriorSceneBaseName = "AFHangar",
                ExteriorShellPrefabName = "STR_AF_Hangar_Prefab",
                YOffset = 0f,
                ScaleAdjustment = new Vector3(1.049f, 1.035f, 1.03f),
                FallbackPosition = new Vector3(161.8379f, 161.5894f, -649.4945f),
                ForceExactPosition = true,

                ObjectsToDestroy = new List<string> { "CONTAINER_InaccessibleGear", "DLM_AFHangar_LGT_Prefab" },
                ObjectsToDisable = new List<string> { "STR_AF_Hangar_Interior_Main_Windows_Glow_Prefab", "STR_AF_Hangar_Interior_Side_Window_Glow_Prefab" },
                EntrySpawnPosition = new Vector3(163.1478f, 162.594f, -617.2457f),
                ExitSpawnPosition = new Vector3(163.1478f, 161.594f, -617.2457f),
                RotationOffset = new Vector3(0f, 300f, 0f),
                DoorSpawnPoints = new List<DoorSpawnPoint>
                {
                    
                    new DoorSpawnPoint {
                    DoorName = "STR_MetalDoorExt_E_Left_Prefab",
                    DoorTransformPosition = new Vector3(163.1364f, 160.9523f, -618.2877f),
                    EntryPosition = new Vector3(162.5694f, 163.3959f, -617.3921f),
                    ExitPosition = new Vector3(163.1478f, 161.594f, -617.2457f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "STR_MetalDoorExt_E_Left_Prefab (1)",
                    DoorTransformPosition = new Vector3(135.5295f, 163.0083f, -665.3468f),
                    EntryPosition = new Vector3(134.3205f, 163.3957f, -665.4089f),
                    ExitPosition = new Vector3(134.8359f, 161.8245f, -665.366f)

                    },

                    new DoorSpawnPoint {
                    DoorName = "STR_MetalDoorExt_E_Left_Prefab (2)",
                    DoorTransformPosition = new Vector3(178.2659f, 168.077f, -662.2448f),
                    EntryPosition = new Vector3(177.8745f, 168.6338f, -662.8293f),
                    ExitPosition = new Vector3(179.0541f, 166.8872f, -662.8561f)

                    },
                },
            },
        };
    }
}
