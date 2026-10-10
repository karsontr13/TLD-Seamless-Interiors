# Seamless Interiors – Integration Guide for Mod Authors

Seamless Interiors (SI) removes the loading screen from building doors. It does that by **cloning each
building's interior scene into the region scene**. The player walks through the door and is still in
`LakeRegion`, just standing inside a clone.

That used to break the assumption most mods make: *"indoors" = a separate scene*. **SI now answers the
vanilla questions for you**: inside one of its buildings, mod code asking for the scene, for the indoor
state or for the fires around it gets the same answers the vanilla game would give inside `CampOffice`.
Most mods therefore need no integration at all.

This guide describes what SI emulates, where it cannot, and the public API (`SeamlessInteriorsApi`) for
the things only SI knows - which of several identical cabins the player is in, for example.

- API version: **1** (SI 2.1.1+)
- Namespace / class: `SeamlessInteriors.SeamlessInteriorsApi` (assembly `SeamlessInteriors`)
- Melon name: `SeamlessInteriors`

---

## 1. What SI answers for you

These are answered for **managed (mod) code only**. The game's own native systems keep seeing the region,
which is what keeps the seamless door working.

| Your mod asks… | Vanilla interior | Inside an SI building |
|---|---|---|
| `Weather.IsIndoorScene()` | `true` | `true` |
| `Weather.IsIndoorEnvironment()` | `true` | `true` |
| `GameManager.m_ActiveScene` | `CampOffice` | `CampOffice` |
| `SceneManager.GetActiveScene().name` | `CampOffice` | `CampOffice` |
| `SceneManager.sceneCount` / `GetSceneAt` / `GetSceneByName` / `Scene.GetRootGameObjects()` | the interior scene and its roots | the building's contents as the only loaded scene; outdoors the region without buildings; SI's emptied template scenes never |
| `Object.FindObjectsOfType` / `FindObjectOfType` | the interior's objects | the entered building's objects and the game's systems; outdoors everything but the buildings |
| `FireManager.m_Fires` / `m_WoodStoves` / `m_Campfires` | the interior's fires | the entered building's fires only |
| Your patches on `GameManager.InstantiatePlayerObject` and on aurora light registration | run when the interior loads | run when the player walks in (see *Setting up a scene when it loads*) |
| Your patches on `QualitySettingsManager.ApplyCurrentQualitySettings` (ModComponent's GearSpawner) | run when the interior loads; first-visit spawns are saved | run once, when the building is first filled in the save; spawns are saved with the building |
| The game's own load and save steps (`GameManager.Awake` / `Start`, `SaveGameSystem.LoadSceneData` / `SaveSceneData`) | the scene being loaded or saved | the region, the scene the game is really loading or saving |
| `OnSceneWasInitialized` for a clone template | never happens | never reaches your mod (SI hides its own loads) |
| A scene name handed back to `SaveGameSystem.SaveGame`, `GameManager.LoadScene` or `SceneTransitionData.m_SceneSaveFilenameCurrent` | the interior | rewritten to the loaded region, so the save is not misfiled |
| Loading the region scene while the player is inside | the player leaves the interior | the region reloads and the player lands where **you** put them |

Outdoors, the same lists hide the buildings' fires, as they would in vanilla where those scenes are not
loaded at all.

**Scene name vs building.** Two cabins can share one vanilla scene (`LakeCabinA`). Mods see that shared
scene name, exactly as they would in vanilla. When you need to tell those cabins apart - separate
contamination, separate stored heat - use the interior **id** from the API (`LakeCabinA_1`, `LakeCabinA_2`).

---

## 2. Where SI cannot help

| Case | What happens | What to do |
|---|---|---|
| Hardcoded world coordinates inside an interior (`player.position.y <= 6` for the hangar's lower level) | The clone sits at the building's place in the region, so world positions you READ differ. Objects you create at interior coordinates during a building's load are moved for you | Convert through the clone root (see *Coordinates inside a building*) |
| `Resources.FindObjectsOfTypeAll` | Still returns the whole region, plus every building | Filter with `GetInteriorIdOf` / `IsInClosedInterior` |
| A closed building's `Update` | Does not run; SI keeps **fires and cooking pots** ticking itself | Catch up on `PlayerEnteredInterior` (see *Time inside closed buildings*) |
| `OnSceneWasLoaded` / `OnSceneWasInitialized` for an interior | Never fire for an SI building | Patch `GameManager.InstantiatePlayerObject` (see *Setting up a scene when it loads*), or use `PlayerEnteredInterior` |

---

## 3. Quick start: soft dependency (recommended)

No reference to SI, no crash when SI is not installed. Copy this class into your mod (net6):

```csharp
using System;
using System.Reflection;
using MelonLoader;
using UnityEngine;

internal static class SeamlessInteriorsBridge
{
    private static Type? s_Api;
    private static Func<string?>? s_PlayerInteriorId;
    private static Func<string?, string?>? s_InteriorSceneName;
    private static Func<string?, bool>? s_IsCloneTemplateScene;
    private static Func<GameObject?, string?>? s_InteriorIdOf;
    private static Func<GameObject?, bool>? s_IsInClosedInterior;
    private static Func<bool>? s_AreInteriorsReady;

    public static bool IsInstalled => s_Api != null;

    // Call from OnLateInitializeMelon: every mod is registered by then.
    public static void Init()
    {
        foreach (MelonBase melon in MelonBase.RegisteredMelons)
        {
            if (melon.Info.Name != "SeamlessInteriors") continue;
            s_Api = melon.GetType().Assembly.GetType("SeamlessInteriors.SeamlessInteriorsApi");
            break;
        }
        if (s_Api == null) return;

        s_PlayerInteriorId     = Bind<Func<string?>>("GetPlayerInteriorId");
        s_InteriorSceneName    = Bind<Func<string?, string?>>("GetInteriorSceneName");
        s_IsCloneTemplateScene = Bind<Func<string?, bool>>("IsCloneTemplateScene");
        s_InteriorIdOf         = Bind<Func<GameObject?, string?>>("GetInteriorIdOf");
        s_IsInClosedInterior   = Bind<Func<GameObject?, bool>>("IsInClosedInterior");
        s_AreInteriorsReady    = s_Api.GetProperty("AreInteriorsReady")?.GetGetMethod()
                                      ?.CreateDelegate(typeof(Func<bool>)) as Func<bool>;
    }

    public static string? PlayerInteriorId => s_PlayerInteriorId?.Invoke();
    public static string? InteriorSceneName(string? id) => s_InteriorSceneName?.Invoke(id);
    public static bool IsCloneTemplateScene(string? scene) => s_IsCloneTemplateScene?.Invoke(scene) ?? false;
    public static string? InteriorIdOf(GameObject? go) => s_InteriorIdOf?.Invoke(go);
    public static bool IsInClosedInterior(GameObject? go) => s_IsInClosedInterior?.Invoke(go) ?? false;
    public static bool AreInteriorsReady => s_AreInteriorsReady?.Invoke() ?? true;

    public static void OnPlayerEntered(Action<string> handler) => Subscribe("PlayerEnteredInterior", handler);
    public static void OnPlayerExited(Action<string> handler) => Subscribe("PlayerExitedInterior", handler);

    private static T? Bind<T>(string name) where T : Delegate =>
        s_Api!.GetMethod(name, BindingFlags.Public | BindingFlags.Static)?.CreateDelegate(typeof(T)) as T;

    private static void Subscribe(string eventName, Action<string> handler) =>
        s_Api?.GetEvent(eventName, BindingFlags.Public | BindingFlags.Static)?.AddEventHandler(null, handler);
}
```

Every wrapper returns the "SI is not installed" answer (`null` / `false` / `true` for readiness) when SI
is missing, so you can call them unconditionally.

---

## 4. Recipes

### Is the player indoors?

```csharp
bool indoors = GameManager.GetWeatherComponent().IsIndoorScene();
```

This already answers `true` inside an SI building, as do `IsIndoorEnvironment()` and the scene name.
`SeamlessInteriorsBridge.PlayerInteriorId != null` says the same thing and also names the building.

### Which building? (per-building state)

Use the **interior id**, never the scene name, as a key: `CampOffice`, `LakeCabinA_2`.
Three lake cabins share the `LakeCabinA` scene but are three ids.

For data that is authored per vanilla scene (config files, profiles), map the id back:

```csharp
string? id = SeamlessInteriorsBridge.PlayerInteriorId;          // "LakeCabinA_2"
string? scene = SeamlessInteriorsBridge.InteriorSceneName(id);   // "LakeCabinA"
```

Buildings with a scene of their own have id == scene name (`CampOffice`), so data your mod saved
before SI was installed keeps matching.

### React to entering / leaving

There is no scene load at the door, so the SI events are the moment to hook:

```csharp
SeamlessInteriorsBridge.OnPlayerEntered(id => MelonLogger.Msg($"entered {id}"));
SeamlessInteriorsBridge.OnPlayerExited(id => MelonLogger.Msg($"left {id}"));
```

- Raised once per real change. A farmhouse → basement move raises **Exited(FarmHouseA)** then
  **Entered(FarmHouseABasement_1)**.
- Entered is raised after the building is opened; Exited after it is closed.
- After loading a save made indoors, Entered can arrive **before the building is built**. If you need its
  objects, check `IsInteriorOpen(id)` or wait for `AreInteriorsReady`.
- Keep handlers fast. An exception is caught and logged by SI (`[API] … dinleyicisi hata verdi`) and does
  not break the door.

### Setting up a scene when it loads

Some mods build a scene as it loads: they spawn objects at the interior's own coordinates, swap objects
they find by name, or collect its aurora lights. When the player walks into an SI building, SI runs
**your** Harmony patches on these vanilla load hooks, with the view above on. The game's own code is not
run again.

| Hook | When SI runs your patches |
|---|---|
| `GameManager.InstantiatePlayerObject` | every time the player walks in, as a door would load the interior |
| `AuroraModularElectrolizer.Initialize`, `AuroraManager.RegisterAuroraElectrolizer` / `RegisterAuroraLightSimple` / `RegisterAuroraActivatedToggle` / `RegisterAuroraField` | right after, for each of the building's active aurora objects, as their `Awake` would |
| `QualitySettingsManager.ApplyCurrentQualitySettings` | once, when the building is first filled in the save |

- Objects you create without a parent during these calls belong to the building. Placed at the vanilla
  interior's coordinates they are moved into the clone; placed inside the building already they stay put.
- When the player walks out, the interior unloads for your mod: objects you created on entry are
  destroyed and objects you switched on or off are put back, so the next entry starts clean.
- Spawns from the first-fill hook are building content and are saved with the building.
- Supported patch parameters: `__instance`, the original's parameters by name or `__0`…, `__state`,
  `__runOriginal`, `__originalMethod`, `__args`, `__result`. A patch asking for anything else is skipped
  (log: `[BINA-YUKLEME]`).
- `OnSceneWasLoaded` / `OnSceneWasInitialized` are **not** replayed: many mods treat them as a full scene
  change, and the region never unloads.

### Objects: which building, and is it closed?

`FireManager`'s lists and `FindObjectsOfType` are already filtered to the place the player is in.
Everything you find another way (`Resources.FindObjectsOfTypeAll`, your own caches) still holds the whole
region:

```csharp
foreach (Container c in Object.FindObjectsOfType<Container>())
{
    if (SeamlessInteriorsBridge.IsInClosedInterior(c.gameObject)) continue; // not in the world right now
    string? building = SeamlessInteriorsBridge.InteriorIdOf(c.gameObject);   // null = outdoors
}
```

`GetInteriorIdOf` also recognises things the player placed indoors that are not parented to the clone
(a campfire built inside), by the building's volume.

### Scanning the scene after a load

Buildings are built behind SI's loading screen after the region loads. A one-time scan at
`OnSceneWasInitialized` misses their contents. Wait for readiness, and rescan on enter/exit if the scan
only includes active objects:

```csharp
if (!SeamlessInteriorsBridge.AreInteriorsReady) return; // try again next frame
```

### Coordinates inside a building

The clone's root keeps the vanilla scene's coordinate system, so a vanilla position turns into the
clone's local space - this is how a rule like "the hangar's lower level is below y = 6" keeps working:

```csharp
GameObject? root = /* SeamlessInteriorsApi.GetInteriorRoot(id) via reflection */;
Vector3 vanillaPos = root.transform.InverseTransformPoint(player.position);
```

### Teleporting the player, and loading scenes

- **Out of a building** (sleepwalking, fast travel…): nothing to do. SI notices the player is outside a
  building without using a door and closes it within about a second (log: `[KAPISIZ-CIKIS]`).
- **Loading the region scene** while the player is inside (how a mod would leave a vanilla interior):
  allowed. The region reloads, the game state is restored as in any transition, and SI leaves the player
  wherever your code puts them - it does not pull them back into the building (log: `[MOD-GECIS]`).
- **Into a building**: not supported. The building is only opened through its door.

### Time inside closed buildings

A closed building is inactive, so its components' `Update` does not run. SI keeps **fires and cooking
pots** running itself. Anything else your mod ticks inside a building (a timer on an item, a decaying
value) must catch up on its own, e.g. by storing the time on `PlayerExitedInterior` and applying the
difference on `PlayerEnteredInterior`.

---

## 5. API reference

All members are static, never throw, and are safe to call from the main menu.

| Member | Returns |
|---|---|
| `const int ApiVersion` | `1`. Read via reflection with `GetField("ApiVersion").GetRawConstantValue()`. |
| `event Action<string> PlayerEnteredInterior` | Interior id the player entered. |
| `event Action<string> PlayerExitedInterior` | Interior id the player left. |
| `bool IsPlayerInsideInterior` | Player is inside an SI building. |
| `string GetPlayerInteriorId()` | Interior id, or `null` outdoors. |
| `string GetPlayerInteriorSceneName()` | Vanilla scene of that building, or `null`. |
| `string[] GetInteriorIds()` | Every building this SI version clones. |
| `string GetInteriorSceneName(string id)` | Vanilla interior scene (`LakeCabinA`), or `null`. |
| `string GetInteriorRegionSceneName(string id)` | Region scene (`LakeRegion`), or `null`. |
| `bool IsInteriorOpen(string id)` | The building is currently open (player inside). |
| `GameObject GetInteriorRoot(string id)` | Clone root, or `null` before it is built. |
| `string GetInteriorIdAt(Vector3 pos)` | Building whose volume holds the position, or `null`. |
| `string GetInteriorIdOf(GameObject go)` | Building an object belongs to (parent or volume), or `null`. |
| `bool IsInClosedInterior(GameObject go)` | Object is part of a building that is closed now. |
| `bool IsCloneTemplateScene(string scene)` | Scene is an SI clone template, not a place the player is. |
| `bool AreInteriorsReady` | SI is not building interiors right now. |

---

## 6. Buildings in SI 2.1.1

Use `GetInteriorIds()` at runtime; this list grows with new SI versions.

| Interior id | Vanilla scene | Region |
|---|---|---|
| `CampOffice` | CampOffice | LakeRegion |
| `SafeHouseA` | SafeHouseA (Trapper's) | LakeRegion |
| `LakeCabinA_1`, `LakeCabinA_2`, `LakeCabinA_3` | LakeCabinA | LakeRegion |
| `LakeCabinB_1` | LakeCabinB | LakeRegion |
| `LakeCabinC_1` | LakeCabinC | LakeRegion |
| `LakeCabinD_1` | LakeCabinD | LakeRegion |
| `LakeCabinE_1` | LakeCabinE | LakeRegion |
| `LakeCabinF_1` | LakeCabinF | LakeRegion |
| `CampTrailerA_1` … `CampTrailerD_1` | TrailerA … TrailerD | LakeRegion |
| `CampTrailerE_1` | DamTrailerB | LakeRegion |
| `FarmHouseA` | FarmHouseA | RuralRegion |
| `FarmHouseABasement_1` | FarmHouseABasement | RuralRegion |
| `QuonsetGasStation` | QuonsetGasStation | CoastalRegion |
| `AFHangar` | AFHangar | AirfieldRegion |
| `GreyMothersHouseA` | GreyMothersHouseA | MountainTownRegion |

---

## 7. Alternative: hard reference

You can reference `SeamlessInteriors.dll` directly and call `SeamlessInteriorsApi` like any class.

- Add `[assembly: MelonOptionalDependencies("SeamlessInteriors")]`.
- Put every SI call in a separate class, and only touch that class after checking the melon is
  registered. Otherwise your mod fails to load its type when SI is absent.
- SI is built for .NET Framework 4.7.2; referencing it from a net6 mod works under MelonLoader.

The reflection bridge above avoids all of this and is the recommended path.

---

## 8. When something still does not fit

SI no longer carries per-mod compatibility code: everything it does is the vanilla answer, given to every
mod at once. If your mod behaves differently inside an SI building than it does inside the vanilla
interior, that is an SI bug - please report it with the mod's name and version, what you expected, and a
MelonLoader log with SI's verbose logging on (F7).

---

## 9. Log tags

| Tag | Meaning |
|---|---|
| `[SAHNE-GORUNUMU]` | The scene view: which interior scene mods are being shown. |
| `[KOK-GORUNUMU]` | The loaded scenes and root objects mods are being shown. |
| `[NESNE-GORUNUMU]` | A `FindObjectsOfType` search a mod made, and how much of it was shown. |
| `[BINA-YUKLEME]` | A building's interior loaded or unloaded for mods; which patches ran, what they created. |
| `[DUNYA-GORUNUMU]` | The fire lists mods are being shown. |
| `[SAHNE-KALKANI]` | A clone template scene load that was kept away from other mods. |
| `[KAYIT-ADI]` | A mod handed an interior scene name back to the game; the region's name was written. |
| `[MOD-GECIS]` | A mod loaded a scene while the player was inside a building. |
| `[VANILLA-ALAN]` | The player entered / left the game's own indoor space of a building. |
| `[KONUM]` | Player entered / left a building (verbose logging). |
| `[KAPISIZ-CIKIS]` | Player left a building without a door; SI closed it. |
| `[API]` | An event handler of another mod threw. |

In game: **F7** toggles verbose logging, **Shift+F10** prints the indoor readout for the current building
(temperatures, the scene and fire counts mods are shown, wind and blizzard values).
