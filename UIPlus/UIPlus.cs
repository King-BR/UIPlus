using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using RaftModLoader;
using HMLLibrary;
using Steamworks;
using HarmonyLib;
using Unity.Netcode;

public class UIPlus : Mod
{
    public static readonly string MOD_NAME = "UIPlus";
    public static readonly string MOD_NAMESPACE = "com.KingBR." + MOD_NAME;
    public static readonly string prefix = "[" + MOD_NAME + "]: ";
    public static Mod self;
    public static bool debugLogging = false;
    public static Harmony harmony;
    public static Dictionary<uint, string> Labels = new Dictionary<uint, string>();
    public static bool labelsLoaded = false;
    public static string WorldLabelDataPath
    {
        get
        {
            string labelDataFile = MOD_NAME + "_labeldata.json";
            string date = new DateTime(SaveAndLoad.WorldToLoad.lastPlayedDateTicks).ToString(SaveAndLoad.dateTimeFormattingSaveFile);
            string text = Path.Combine(SaveAndLoad.WorldPath, SaveAndLoad.WorldToLoad.name, date);
            if (Directory.Exists(text))
                return Path.Combine(text, labelDataFile);
            return Path.Combine(SaveAndLoad.WorldPath, SaveAndLoad.WorldToLoad.name, date + SaveAndLoad.latestStringNameEnding, labelDataFile);
        }
    }

    public void Start()
    {
        self = this;
        harmony = new Harmony(MOD_NAMESPACE);
        harmony.PatchAll();
        Debug.Log(prefix + "Mod has been loaded!");

        if (LoadSceneManager.IsGameSceneLoaded)
        {
            LoadLabels();

            GameManager singleton = SingletonGeneric<GameManager>.Singleton;
            if (singleton.gameObject.GetComponent<LabelWatcher>() != null)
                return;
            singleton.gameObject.AddComponent<LabelWatcher>();
        }
    }

    public void OnModUnload() => UnloadMod();

    // Mod.UnloadMod() is the documented override point for cleanup
    // (https://api.raftmodding.com/modding-api/mod) - OnModUnload() above isn't part of
    // the current documented API, so we route both through here to be safe either way.
    public override void UnloadMod()
    {
        harmony.UnpatchAll(harmony.Id);
        labelsLoaded = false;
        Debug.Log(prefix + "Mod has been unloaded!");
        base.UnloadMod();
    }

    public override void WorldEvent_WorldUnloaded()
    {
        Labels = new Dictionary<uint, string>();
        labelsLoaded = false;
    }
    public override void WorldEvent_WorldLoaded()
    {
        LoadLabels();

        // WorldEvent_OnPlayerConnected isn't available on this Mod base class version, so
        // instead of the host pushing a sync when someone joins, a freshly-loaded non-host
        // client asks for one here. The host answers by broadcasting a full sync (see
        // ProcessIncomingNetworkMessages) - slightly more traffic than a targeted send, but
        // RAPI.SendNetworkMessage has no single-recipient overload available to us anyway.
        if (!Raft_Network.IsHost) RequestFullSync();
    }

    public static void LoadLabels()
    {
        if (!LoadSceneManager.IsGameSceneLoaded)
        {
            Debug.Log(prefix + "Unable to load storage labels as you aren't in a game");
            return;
        }

        if (!Raft_Network.IsHost)
        {
            try
            {
                Debug.Log(prefix + "Player is not host, loading temporary labels data");
                string tmpPath = Path.Combine(Application.persistentDataPath, "ModData", MOD_NAME, "tmpLabeldata_" + SaveAndLoad.WorldGuid.ToString() + ".json");

                if (!File.Exists(tmpPath))
                {
                    Labels = new Dictionary<uint, string>();
                }
                else
                {
                    string tmpStr = File.ReadAllText(tmpPath);

                    if (debugLogging) Debug.Log(prefix + "pre de-serialize " + tmpStr);
                    Labels = JsonConvert.DeserializeObject<Dictionary<uint, string>>(tmpStr) ?? throw new Exception("De-serialization of tmpFile labels failed");
                }
            }
            catch (Exception _e)
            {
                Debug.LogError(prefix + "Failed to load tmp labels: " + _e.Message);
            }

            labelsLoaded = true;
            return;
        }

        try
        {
            if (!File.Exists(WorldLabelDataPath))
            {
                Labels = new Dictionary<uint, string>();
                return;
            }

            string tmpStr = File.ReadAllText(WorldLabelDataPath);

            if (debugLogging) Debug.Log(prefix + "pre de-serialize " + tmpStr);
            Labels = JsonConvert.DeserializeObject<Dictionary<uint, string>>(tmpStr) ?? throw new Exception("De-serialization of labels failed");
            labelsLoaded = true;
        }
        catch (Exception _e)
        {
            Debug.LogError(prefix + "Failed to load labels: " + _e.Message);
        }
    }

    public static void SaveLabels()
    {
        if (!Raft_Network.IsHost)
        {
            Debug.Log(prefix + "Player is not host, saving temporary labels data");
            string tmpPath = Path.Combine(Application.persistentDataPath, "ModData", MOD_NAME, "tmpLabeldata_" + SaveAndLoad.WorldGuid.ToString() + ".json");
            SaveLabels(tmpPath);
        }
        else SaveLabels(WorldLabelDataPath);
    }

    public static void SaveLabels(string path)
    {
        if (debugLogging) Debug.Log(prefix + "Try to save storage labels data to " + path);

        if (!LoadSceneManager.IsGameSceneLoaded)
        {
            if (debugLogging) Debug.Log(prefix + "Unable to save storage labels as there is no game loaded");
            return;
        }

        try
        {
            string tmpStr = JsonConvert.SerializeObject(Labels);
            File.WriteAllText(path, tmpStr);
        }
        catch (Exception _e)
        {
            Debug.LogError(prefix + "Failed to save labels: " + _e.Message);
        }
    }

    public static string FormatTankCapacity(string format, float currentAmount, float maxAmount)
    {
        string formatedCapacity = "";

        if (format.Contains("+"))
        {
            string[] formats = format.Split('+');
            string[] tmpArr = new string[] { };
            foreach (string f in formats)
            {
                tmpArr = tmpArr.AddToArray(FormatTankCapacity(f.Trim(), currentAmount, maxAmount));
            }

            tmpArr = tmpArr.Where(val => !val.IsNullOrEmpty()).ToArray();

            foreach (string tmpStr in tmpArr)
            {
                if (formatedCapacity.IsNullOrEmpty())
                {
                    formatedCapacity = tmpStr;
                }
                else formatedCapacity += " (" + tmpStr + ")";
            }
        }
        else
        {
            switch (format)
            {
                case "number":
                    formatedCapacity = (int)Math.Round(currentAmount) + "/" + (int)Math.Round(maxAmount);
                    break;
                case "percent":
                    formatedCapacity = (currentAmount * 100 / maxAmount).ToString("0.00") + "%";
                    break;
                case "percent rounded":
                    formatedCapacity = (int)Math.Round(currentAmount * 100 / maxAmount) + "%";
                    break;
                default:
                    Debug.LogError(prefix + "Invalid format '" + format + "'. Send this error to KingBR#3793 in the RaftModding discord server");
                    break;
            }
        }
        return formatedCapacity;
    }

    // The Plant component exposes an "item" field (Item_Base) that is specifically meant
    // to describe the plant/tree that is growing - this is the correct source for the name.
    // Previously we used Helper.GetTerm(plant.pickupComponent.pickupTerm), but pickupTerm is
    // empty while the plant is still growing (it's only meant for the harvested pickup), so
    // it always resolved to the generic fallback term ("Item"), which is the bug the "Trees
    // listed as 'item'" workaround was papering over.
    public static string GetPlantName(Plant plant)
    {
        Item_Base plantItem = plant != null ? plant.item : null;

        if (plantItem == null || plantItem.settings_Inventory == null)
        {
            // Fallback to the old behaviour for any plant that, for whatever reason
            // (e.g. a mod adding custom plants without setting the item field), doesn't
            // have its item assigned, so we never regress to a hard crash.
            string fallback = Helper.GetTerm(plant?.pickupComponent?.pickupTerm)?.Split('@')[0];
            if (fallback.IsNullOrEmpty()) return "Tree";
            return fallback.ToLower().Trim().Equals("item") ? "Tree" : fallback;
        }

        string localizationTerm = plantItem.settings_Inventory.LocalizationTerm;
        string name = localizationTerm.IsNullOrEmpty() ? null : Helper.GetTerm(localizationTerm)?.Split('@')[0];

        if (name.IsNullOrEmpty())
        {
            // No translation found for the term (or it's unset), fall back to the
            // item's raw display name instead of a localization key/placeholder.
            name = plantItem.settings_Inventory.DisplayName;
        }

        return name.IsNullOrEmpty() ? "Tree" : name;
    }

    public static string FormatCropplotPlantList(string format, string timeFormat, Cropplot _cropplot)
    {
        if (!_cropplot.ContainsCrops) return "No Plants";

        string formatted = null;
        string formatTime(float time)
        {
            string secLeft = (time % 60) > 10 ? $"{(int)(time % 60)}" : $"0{(int)(time % 60)}";
            return $"{(int)(time / 60)}:{secLeft}";
        }

        switch (format)
        {
            case "grouped":
                Dictionary<string, int> plantCountDict = new Dictionary<string, int>();
                Dictionary<string, float[]> plantGrowTime = new Dictionary<string, float[]>();
                bool needWater = false;
                foreach (PlantationSlot ps in _cropplot.GetSlots())
                {
                    if (ps.busy)
                    {
                        if (!ps.hasWater) needWater = true;

                        float growTimeSec = Traverse.Create(ps.plant).Field("growTimeSec").GetValue<float>();
                        float growLeft = Math.Abs(ps.plant.GetGrowTimer() - growTimeSec);
                        string tmp = GetPlantName(ps.plant);

                        if (!plantCountDict.ContainsKey(tmp))
                        {
                            plantCountDict.Add(tmp, 1);
                            plantGrowTime.Add(tmp, new float[] { growLeft });
                        }
                        else
                        {
                            plantCountDict[tmp]++;
                            plantGrowTime[tmp] = plantGrowTime[tmp].AddToArray(growLeft);
                        }
                    }
                }

                foreach (string k in plantCountDict.Keys)
                {
                    string timeLeft = null;

                    switch (timeFormat)
                    {
                        case "closer":
                            float min = plantGrowTime[k].Where(v => v > 0).Count() > 0 ? plantGrowTime[k].Where(v => v > 0).Min() : 0;
                            if (min == 0)
                            {
                                timeLeft = "Ready";
                            }
                            else timeLeft = formatTime(min);
                            break;
                        case "farthest":
                            float max = plantGrowTime[k].Where(v => v > 0).Count() > 0 ? plantGrowTime[k].Where(v => v > 0).Max() : 0;
                            if (max == 0)
                            {
                                timeLeft = "Ready";
                            }
                            else timeLeft = formatTime(max);
                            break;
                        case "average":
                            float avg = plantGrowTime[k].Where(v => v > 0).Count() > 0 ? plantGrowTime[k].Where(v => v > 0).Average() : 0;
                            if (avg == 0)
                            {
                                timeLeft = "Ready";
                            }
                            else timeLeft = formatTime(avg);
                            break;
                        default:
                            Debug.LogError(prefix + "Unknown time format '" + timeFormat + "'. Send this error to KingBR#3793 in the RaftModding discord server: https://discord.gg/Q8PaZ42FrC");
                            return "Unknown time format, see error on console (press F10)";
                    }

                    if (formatted.IsNullOrEmpty())
                    {
                        formatted = $"{k} x{plantCountDict[k]}";
                    }
                    else formatted += $"\n{k} x{plantCountDict[k]}";

                    if (!timeLeft.IsNullOrEmpty()) formatted += $" - {timeLeft}";
                    if (needWater) formatted += " [Needs Water!]";
                }
                break;
            case "list":
                foreach (PlantationSlot ps in _cropplot.GetSlots())
                {
                    if (ps.busy)
                    {
                        string tmp = GetPlantName(ps.plant);

                        if (formatted.IsNullOrEmpty())
                        {
                            formatted = $"{ps.plant.plantationSlotIndex + 1}: {tmp}";
                        }
                        else formatted += $"\n{ps.plant.plantationSlotIndex + 1}: {tmp}";

                        float growTimeSec = Traverse.Create(ps.plant).Field("growTimeSec").GetValue<float>();
                        float growLeft = Math.Abs(ps.plant.GetGrowTimer() - growTimeSec);
                        string timeLeft = formatTime(growLeft);

                        if (!ps.hasWater)
                        {
                            formatted += $" - {timeLeft} (Needs Water!)";
                        }
                        else if (growLeft == 0)
                        {
                            formatted += " - Ready";
                        }
                        else formatted += $" - {timeLeft}";

                        //formatted += $" - {ps.plant is Plant_Palm}";
                    }
                }
                break;
            default:
                Debug.LogError(prefix + "Unknown format '" + format + "'. Send this error to KingBR#3793 in the RaftModding discord server: https://discord.gg/Q8PaZ42FrC");
                formatted = "Unkown format, see error in console (press F10)";
                break;
        }

        if (formatted.IsNullOrEmpty()) return "No Plants";
        return formatted;
    }

    #region Console commands
    [ConsoleCommand(name: "label", docs: "Syntax: label [label name] - Label the storage you are currently looking, if used without args it will remove the current label")]
    public static string LabelCmd(string[] args)
    {
        if (!LoadSceneManager.IsGameSceneLoaded) return prefix + "You aren't in a world";
        if (ExtraSettingsAPI_Loaded && !ExtraSettingsAPI_GetCheckboxState("enableStorageLabels")) return prefix + "Storage Labels is disabled is this world!";

        if (Patch_Storage_Small.storageInstance == null) return prefix + "You arent looking at any storage right now";

        if (args.Length == 0)
        {
            if (Labels.ContainsKey(Patch_Storage_Small.storageInstance.ObjectIndex)) Labels.Remove(Patch_Storage_Small.storageInstance.ObjectIndex);
            SaveLabels();
            BroadcastLabelUpdate(Patch_Storage_Small.storageInstance.ObjectIndex, "");
            return prefix + "Label removed";
        }

        if (Labels.ContainsKey(Patch_Storage_Small.storageInstance.ObjectIndex))
        {
            Labels[Patch_Storage_Small.storageInstance.ObjectIndex] = string.Join(" ", args);
        }
        else
        {
            Labels.Add(Patch_Storage_Small.storageInstance.ObjectIndex, string.Join(" ", args));
        }
        SaveLabels();
        BroadcastLabelUpdate(Patch_Storage_Small.storageInstance.ObjectIndex, string.Join(" ", args));
        return prefix + "Label '" + string.Join(" ", args) + "' added";
    }

    [ConsoleCommand(name: "deletelabels", docs: "Syntax: deletelabels - Delete all labels from the world")]
    public static string DeleteAllLabels(string[] args)
    {
        if (!LoadSceneManager.IsGameSceneLoaded) return prefix + "You aren't in a world";
        if (ExtraSettingsAPI_Loaded && !ExtraSettingsAPI_GetCheckboxState("enableStorageLabels")) return prefix + "Storage Labels is disabled is this world!";

        Labels = new Dictionary<uint, string>();
        SaveLabels();
        BroadcastFullSync();
        return prefix + "Deleted all labels from the world";
    }

    [ConsoleCommand(name: "toggleUIPlusDebug", docs: "Syntax: toggleUIPlusDebug - toggle debug logging of UI+ mod")]
    public static string toggleDebug(string[] args)
    {
        debugLogging = !debugLogging;
        return prefix + "Debug logging has been turned " + (debugLogging ? "on" : "off");
    }
    #endregion

    #region Harmony patches
    [HarmonyPatch(typeof(Storage_Small))]
    internal class Patch_Storage_Small
    {
        public static Storage_Small storageInstance = null;
        public static bool canOpenLabelMenu = false;

        [HarmonyPatch("OnFinishedPlacement")]
        [HarmonyPostfix]
        static void OnFinishedPlacement(Storage_Small __instance)
        {
            if (ExtraSettingsAPI_Loaded && !ExtraSettingsAPI_GetCheckboxState("enableStorageLabels")) return;
            if (debugLogging) Debug.Log(prefix + "New storage placed. ID: " + __instance.ObjectIndex);

            Labels.Add(__instance.ObjectIndex, "");
        }

        [HarmonyPatch("OnDestroy")]
        [HarmonyPostfix]
        static void OnDestroy(Storage_Small __instance)
        {
            if (debugLogging) Debug.Log(prefix + "Storage destroyed. ID: " + __instance.ObjectIndex);
            if (Labels.ContainsKey(__instance.ObjectIndex))
                Labels.Remove(__instance.ObjectIndex);
        }

        [HarmonyPatch("OnIsRayed")]
        [HarmonyPostfix]
        static void OnIsRayed(Storage_Small __instance, CanvasHelper ___canvas)
        {
            if (ExtraSettingsAPI_Loaded && !ExtraSettingsAPI_GetCheckboxState("enableStorageLabels")) return;
            if (CanvasHelper.ActiveMenu == MenuType.None && !PlayerItemManager.IsBusy && ___canvas.CanOpenMenu && Helper.LocalPlayerIsWithinDistance(__instance.transform.position, Player.UseDistance + 0.5f))
            {
                if (!labelsLoaded) LoadLabels();

                canOpenLabelMenu = true;
                storageInstance = __instance;
                if (!Labels.ContainsKey(__instance.ObjectIndex))
                    Labels.Add(__instance.ObjectIndex, "");
            }
            else
            {
                canOpenLabelMenu = false;
                storageInstance = null;
            }
        }

        [HarmonyPatch("OnRayEnter")]
        [HarmonyPostfix]
        static void OnRayEnter(Storage_Small __instance)
        {
            if (CanvasHelper.ActiveMenu == MenuType.None && !PlayerItemManager.IsBusy && Helper.LocalPlayerIsWithinDistance(__instance.transform.position, Player.UseDistance + 0.5f))
                if (debugLogging) Debug.Log(prefix + "RayEnter " + __instance.name);
        }

        [HarmonyPatch("OnRayExit")]
        [HarmonyPostfix]
        static void OnRayExit()
        {
            storageInstance = null;
            canOpenLabelMenu = false;
        }
    }

    [HarmonyPatch(typeof(Tank))]
    internal class Patch_Tank
    {
        public static Tank tankInstance = null;

        [HarmonyPatch("OnIsRayed")]
        [HarmonyPostfix]
        private static void OnIsRayed(Tank __instance, DisplayTextManager ___displayText)
        {
            if (ExtraSettingsAPI_Loaded && !ExtraSettingsAPI_GetCheckboxState("enableShowTankCapacity")) return;
            if (!(CanvasHelper.ActiveMenu == MenuType.None && !PlayerItemManager.IsBusy && Helper.LocalPlayerIsWithinDistance(__instance.transform.position, Player.UseDistance + 0.5f))) return;

            string style = __instance.CurrentTankAmount + "/" + __instance.maxCapacity;
            if (ExtraSettingsAPI_Loaded)
            {
                string styleFormat = ExtraSettingsAPI_GetComboboxSelectedItem("tankCapacityStyle");
                if (debugLogging) Debug.Log(prefix + "Style: " + styleFormat);
                style = FormatTankCapacity(styleFormat, __instance.CurrentTankAmount, __instance.maxCapacity);
            }

            if (__instance.name.Equals("WaterTank"))
            {
                ItemInstance playerItem = RAPI.GetLocalPlayer().Inventory.GetSelectedHotbarItem();
                if (playerItem == null || !playerItem.UniqueName.ToLower().Contains("water") || __instance.IsFull)
                {
                    ___displayText.ShowText(style, 0, false, 0);
                    tankInstance = null;
                }
                else tankInstance = __instance;

                return;
            }

            if (__instance.IsFull)
            {
                ___displayText.ShowText(style, 0, false, 0);
            }
            else tankInstance = __instance;
        }

        [HarmonyPatch("OnRayEnter")]
        [HarmonyPostfix]
        static void OnRayEnter(Tank __instance)
        {
            if (ExtraSettingsAPI_Loaded && !ExtraSettingsAPI_GetCheckboxState("enableShowTankCapacity")) return;
            if (debugLogging && __instance != null)
            {
                Debug.Log(prefix + "RayEnter " + __instance.name);
                Debug.Log(prefix + "Item: " + RAPI.GetLocalPlayer().Inventory.GetSelectedHotbarItem().UniqueName);
            }
        }

        [HarmonyPatch("OnRayExit")]
        [HarmonyPostfix]
        static void OnRayExit()
        {
            if (ExtraSettingsAPI_Loaded && !ExtraSettingsAPI_GetCheckboxState("enableShowTankCapacity")) return;
            tankInstance = null;
        }
    }

    [HarmonyPatch(typeof(Cropplot))]
    public class Patch_Cropplot
    {
        public static Cropplot cropplotInstance;

        [HarmonyPatch("OnIsRayed")]
        [HarmonyPostfix]
        static void OnIsRayed(Cropplot __instance, CanvasHelper ___canvas, bool ___showingText)
        {
            if (!ExtraSettingsAPI_GetCheckboxState("enableCropplotPlantList"))
            {
                cropplotInstance = null;
            }
            else
            {
                ItemInstance selectedItem = ___showingText ? RAPI.GetLocalPlayer().Inventory.GetSelectedHotbarItem() : null;
                bool holdingWater = selectedItem != null && selectedItem.UniqueName.ToLower().Contains("water");

                if (!___showingText || !holdingWater)
                {
                    string styleFormat = ExtraSettingsAPI_Loaded ? ExtraSettingsAPI_GetComboboxSelectedItem("cropplotPlantListStyle") : "grouped";
                    string timeFormat = ExtraSettingsAPI_Loaded ? ExtraSettingsAPI_GetComboboxSelectedItem("cropplotPlantListTimeStyle") : "closer";
                    string formattedStr = FormatCropplotPlantList(styleFormat, timeFormat, __instance);

                    ___canvas.displayTextManager.ShowText(formattedStr, 0, false, 0);
                }
            }

            cropplotInstance = __instance;
        }

        [HarmonyPatch("OnRayExit")]
        [HarmonyPostfix]
        static void OnRayExit()
        {
            cropplotInstance = null;
        }
    }

    [HarmonyPatch(typeof(Helper), "GetTerm")]
    internal class Patch_DisplayText
    {
        private static void Postfix(ref string __result, string term)
        {
            if (CanvasHelper.ActiveMenu != MenuType.None) return;

            if (Patch_Storage_Small.storageInstance != null && Labels.ContainsKey(Patch_Storage_Small.storageInstance.ObjectIndex) && !Labels[Patch_Storage_Small.storageInstance.ObjectIndex].IsNullOrEmpty() && !term.IsNullOrEmpty() && term.Equals("Game/Open"))
            {
                if (ExtraSettingsAPI_Loaded && !ExtraSettingsAPI_GetCheckboxState("enableStorageLabels")) return;
                __result += "\n" + Labels[Patch_Storage_Small.storageInstance.ObjectIndex];
                __result = __result.Replace("\\n", "\n");
                return;
            }

            if (Patch_Tank.tankInstance != null && !term.IsNullOrEmpty() && (term.Equals("Game/RequiredItemX") || term.Equals("Game/PlaceItemX")))
            {
                string style = Patch_Tank.tankInstance.CurrentTankAmount + "/" + Patch_Tank.tankInstance.maxCapacity;

                if (ExtraSettingsAPI_Loaded)
                {
                    if (!ExtraSettingsAPI_GetCheckboxState("enableShowTankCapacity")) return;

                    string styleFormat = ExtraSettingsAPI_GetComboboxSelectedItem("tankCapacityStyle");
                    if (debugLogging) Debug.Log(prefix + "Style: " + styleFormat);
                    style = FormatTankCapacity(styleFormat, Patch_Tank.tankInstance.CurrentTankAmount, Patch_Tank.tankInstance.maxCapacity);
                }

                __result += "\n" + style;
            }
        }
    }

    [HarmonyPatch(typeof(SaveAndLoad), "Save")]
    static class Patch_SaveGame
    {
        static void Postfix(string filename)
        {
            string[] path = filename.Split(new char[] { '\\', '/' });
            filename = "";
            for (int i = 0; i < path.Length - 1; i++)
                filename += path[i] + "\\";
            if (filename.EndsWith(SaveAndLoad.latestStringNameEnding + "\\"))
                SaveLabels(filename + MOD_NAME + "_labeldata.json");
        }
    }

    [HarmonyPatch(typeof(LoadGameBox), "Button_LoadGame")]
    internal class Patch_LoadGame
    {
        static void Postfix() => LoadLabels();
    }

    [HarmonyPatch(typeof(NewGameBox), "Button_CreateNewGame")]
    static class Patch_NewGame
    {
        static void Postfix()
        {
            Labels = new Dictionary<uint, string>();
            labelsLoaded = true;
        }
    }

    [HarmonyPatch(typeof(LoadSceneManager), "LoadScene")]
    static class Patch_UnloadWorld
    {
        static void Postfix(ref string sceneName)
        {
            if (sceneName == Raft_Network.MenuSceneName)
                Labels = new Dictionary<uint, string>();
        }
    }

    [HarmonyPatch(typeof(Network_Player), "Start")]
    internal class WatcherInjector_Patch
    {
        private static void Postfix()
        {
            GameManager singleton = SingletonGeneric<GameManager>.Singleton;
            if (singleton.gameObject.GetComponent<LabelWatcher>() != null)
                return;
            singleton.gameObject.AddComponent<LabelWatcher>();
        }
    }
    #endregion

    #region Multiplayer sync
    // Custom message types for the RaftModLoader network message bus (RAPI.SendNetworkMessage /
    // RAPI.ListenForNetworkMessagesOnChannel). We can't add cases to the game's own `Messages`
    // enum, so - as documented at https://www.raftmodding.com/api/v1/docs/rapi - we pick values
    // well above the vanilla enum's range (currently <300 entries) to avoid any collision.
    private enum UIPlusMessages
    {
        SetLabel = 8511,
        SyncLabels = 8512,
        RequestSync = 8513
    }

    // Dedicated channel for UIPlus network traffic, separate from the default RAPI channel (2)
    // and the game's own NetworkChannel values, so we don't collide with other mods/traffic.
    private const int NETWORK_CHANNEL = 8551;

    // Sent whenever a single storage's label changes (created, edited, or cleared).
    // Broadcast to every other connected player so their copy of Labels stays in sync live.
    [Serializable]
    internal class Message_UIPlus_SetLabel : Message
    {
        public uint objectIndex;
        public string label;

        public Message_UIPlus_SetLabel() { }

        public Message_UIPlus_SetLabel(uint objectIndex, string label) : base((Messages)UIPlusMessages.SetLabel)
        {
            this.objectIndex = objectIndex;
            this.label = label ?? "";
        }

        // objectIndex is sent as a string rather than a native uint - FastBufferWriter's
        // generic primitive overload needs a marker type (ForPrimitives) that isn't
        // reachable the way we referenced it, but the plain string overload works fine,
        // so we sidestep the issue entirely instead of guessing at the right qualification.
        public override void SerializeFast(FastBufferWriter writer)
        {
            base.SerializeFast(writer);
            writer.WriteValueSafe(objectIndex.ToString(), false);
            writer.WriteValueSafe(label, false);
        }

        public override void DeserializeFast(FastBufferReader reader)
        {
            base.DeserializeFast(reader);
            string objectIndexStr;
            reader.ReadValue(out objectIndexStr, false);
            uint.TryParse(objectIndexStr, out objectIndex);
            reader.ReadValue(out label, false);
        }
    }

    // Sent by the host in response to a RequestSync (and after "deletelabels") with the
    // full, authoritative label set.
    [Serializable]
    internal class Message_UIPlus_SyncLabels : Message
    {
        public string labelsJson;

        public Message_UIPlus_SyncLabels() { }

        public Message_UIPlus_SyncLabels(Dictionary<uint, string> labels) : base((Messages)UIPlusMessages.SyncLabels)
        {
            labelsJson = JsonConvert.SerializeObject(labels ?? new Dictionary<uint, string>());
        }

        public override void SerializeFast(FastBufferWriter writer)
        {
            base.SerializeFast(writer);
            writer.WriteValueSafe(labelsJson, false);
        }

        public override void DeserializeFast(FastBufferReader reader)
        {
            base.DeserializeFast(reader);
            reader.ReadValue(out labelsJson, false);
        }
    }

    // Shared guard for all networking methods below: no point touching the network
    // bus outside an active multiplayer game.
    private static bool CanUseNetworkMessaging()
    {
        return !Raft_Network.InSinglePlayerMode && LoadSceneManager.IsGameSceneLoaded;
    }

    // Broadcasts a single label change to every other currently connected player.
    // Safe to call even in singleplayer (it just becomes a no-op).
    public static void BroadcastLabelUpdate(uint objectIndex, string label)
    {
        if (!CanUseNetworkMessaging()) return;

        try
        {
            RAPI.SendNetworkMessage(new Message_UIPlus_SetLabel(objectIndex, label), NETWORK_CHANNEL, EP2PSend.k_EP2PSendReliable, Target.Other);
        }
        catch (Exception _e)
        {
            Debug.LogError(prefix + "Failed to broadcast label update: " + _e.Message);
        }
    }

    // Broadcasts the full current label set to every other currently connected player.
    // RAPI.SendNetworkMessage has no single-recipient overload available here, so a
    // broadcast is the only option - harmless, since applying the same set twice is a no-op.
    public static void BroadcastFullSync()
    {
        if (!CanUseNetworkMessaging()) return;

        try
        {
            RAPI.SendNetworkMessage(new Message_UIPlus_SyncLabels(Labels), NETWORK_CHANNEL, EP2PSend.k_EP2PSendReliable, Target.Other);
        }
        catch (Exception _e)
        {
            Debug.LogError(prefix + "Failed to broadcast full label sync: " + _e.Message);
        }
    }

    // Sent by a client right after its world loads, asking the host for the current label
    // set (see WorldEvent_WorldLoaded). Takes the place of a host-side "player connected"
    // push, since that hook isn't available on this Mod base class version.
    public static void RequestFullSync()
    {
        if (!CanUseNetworkMessaging()) return;

        try
        {
            RAPI.SendNetworkMessage(new Message((Messages)UIPlusMessages.RequestSync), NETWORK_CHANNEL, EP2PSend.k_EP2PSendReliable, Target.Other);
        }
        catch (Exception _e)
        {
            Debug.LogError(prefix + "Failed to request label sync: " + _e.Message);
        }
    }

    // Polled every frame from LabelWatcher. Drains any pending UIPlus network messages and
    // applies them to the local Labels dictionary.
    public static void ProcessIncomingNetworkMessages()
    {
        if (!CanUseNetworkMessaging()) return;

        // Cap iterations per frame as a safety net so a burst of messages can't stall a frame.
        for (int i = 0; i < 32; i++)
        {
            NetworkMessage netMessage;
            try
            {
                netMessage = RAPI.ListenForNetworkMessagesOnChannel(NETWORK_CHANNEL);
            }
            catch (Exception _e)
            {
                Debug.LogError(prefix + "Failed to listen for network messages: " + _e.Message);
                return;
            }

            if (netMessage == null) return;

            try
            {
                if (netMessage.message.Type == (Messages)UIPlusMessages.SetLabel)
                {
                    Message_UIPlus_SetLabel msg = netMessage.message as Message_UIPlus_SetLabel;
                    if (msg != null) Labels[msg.objectIndex] = msg.label;
                }
                else if (netMessage.message.Type == (Messages)UIPlusMessages.SyncLabels)
                {
                    Message_UIPlus_SyncLabels msg = netMessage.message as Message_UIPlus_SyncLabels;
                    if (msg != null)
                    {
                        Labels = JsonConvert.DeserializeObject<Dictionary<uint, string>>(msg.labelsJson) ?? new Dictionary<uint, string>();
                        labelsLoaded = true;
                        if (debugLogging) Debug.Log(prefix + "Received full label sync (" + Labels.Count + " labels)");
                    }
                }
                else if (netMessage.message.Type == (Messages)UIPlusMessages.RequestSync)
                {
                    if (Raft_Network.IsHost) BroadcastFullSync();
                }
            }
            catch (Exception _e)
            {
                Debug.LogError(prefix + "Failed to apply incoming label message: " + _e.Message);
            }
        }
    }
    #endregion

    #region Extra Settings API
    public static bool ExtraSettingsAPI_Loaded;

    public void ExtraSettingsAPI_ButtonPress(string name)
    {
        if (!ExtraSettingsAPI_Loaded || !LoadSceneManager.IsGameSceneLoaded) return;
        if (debugLogging) Debug.Log(prefix + "Pressed button '" + name + "'");

        switch (name)
        {
            case "deletelabelsWorld":
                Debug.Log(DeleteAllLabels(new string[] { }));
                break;
        }
    }

    public void ExtraSettingsAPI_Load() { }
    public static string ExtraSettingsAPI_GetInputValue(string SettingName) => "";
    public static bool ExtraSettingsAPI_GetCheckboxState(string SettingName) => false;
    public static string ExtraSettingsAPI_GetComboboxSelectedItem(string SettingName) => "";
    public static KeyCode ExtraSettingsAPI_GetKeybindMain(string SettingName) => KeyCode.None;
    public static KeyCode ExtraSettingsAPI_GetKeybindAlt(string SettingName) => KeyCode.None;
    #endregion

    // Drives the game's own trophy-renaming text box (TextWriterManager/TextWriterObject,
    // normally used by TrophyTextwriter) for storage labels instead of a custom OnGUI popup,
    // so the label editor looks and behaves exactly like vanilla's text entry UI.
    //
    // TextWriterObject is designed for a real, networked, placed-in-world object (a trophy).
    // We don't want any of that - no NetworkID registration, no save-file entry, and
    // critically, no networked "sign edited" broadcast (which would reference a bogus
    // object index). So we create a bare, un-registered TextWriterObject purely as a
    // vehicle to open the same UI, and Harmony-patch the two places that would otherwise
    // either crash (null field) or misbehave (send a network message about a fake object).
    internal static class LabelTextWriter
    {
        private static TextWriterObject holder;
        private static bool isEditingLabel;
        private static uint pendingStorageIndex;

        private static void EnsureHolderExists()
        {
            if (holder != null) return;

            GameObject holderObject = new GameObject("UIPlus_LabelTextWriterHolder");
            holderObject.transform.position = new Vector3(0f, 10000f, 0f);
            // Keep the object inactive while wiring it up so Awake() (which touches the
            // text mesh) doesn't run before the text mesh actually exists.
            holderObject.SetActive(false);

            TextMeshPro textMesh = holderObject.AddComponent<TextMeshPro>();
            MeshRenderer meshRenderer = holderObject.GetComponent<MeshRenderer>();
            if (meshRenderer != null) meshRenderer.enabled = false;

            holder = holderObject.AddComponent<TextWriterObject>();
            Traverse.Create(holder).Field("textMesh").SetValue(textMesh);

            holderObject.SetActive(true);
        }

        public static void Open(uint storageIndex, string currentLabel)
        {
            if (isEditingLabel) return;

            CanvasHelper canvas = ComponentManager<CanvasHelper>.Value;
            TextWriterManager writerManager = ComponentManager<TextWriterManager>.Value;
            if (canvas == null || writerManager == null) return;
            if (CanvasHelper.ActiveMenu != MenuType.None || writerManager.IsWriting) return;

            EnsureHolderExists();
            holder.SetText(currentLabel ?? "", false);

            pendingStorageIndex = storageIndex;
            isEditingLabel = true;

            ChatTextFieldController chat = ComponentManager<ChatTextFieldController>.Value;
            if (chat != null) chat.DisableChatTyping();

            canvas.OpenMenuCloseOther(MenuType.TextWriter, true);
            GamepadCursor cursor = canvas.GetComponent<GamepadCursor>();
            if (cursor != null) cursor.SetLayer(GamepadCursorLayer.PopupWindows);

            writerManager.StartWriting(holder);
        }

        // Mirrors the UI-closing side effects of the vanilla methods below, minus the
        // networked broadcast (see the patches for why).
        private static void CloseSession()
        {
            Network_Player localPlayer = RAPI.GetLocalPlayer();
            if (localPlayer != null && !localPlayer.CarryingComponent.IsCarrying)
                PlayerItemManager.IsBusy = false;

            CanvasHelper canvas = ComponentManager<CanvasHelper>.Value;
            if (canvas != null)
            {
                canvas.CloseMenu(MenuType.TextWriter);
                GamepadCursor cursor = canvas.GetComponent<GamepadCursor>();
                if (cursor != null) cursor.SetLayer(GamepadCursorLayer.Inventory);
            }

            if (localPlayer != null) localPlayer.PersonController.IsMovementFree = true;

            isEditingLabel = false;
        }

        // The vanilla method unconditionally builds and RPCs/sends a Sign_OnEdit message
        // using the TextWriterObject's ObjectIndex - which our holder never had assigned
        // (we skip Initialize() on purpose). Letting that run would broadcast a bogus
        // message to other players, so when it's our session we handle the input field
        // ourselves and skip the original method entirely.
        [HarmonyPatch(typeof(TextWriterManager), "Button_Finished")]
        internal static class Patch_ButtonFinished
        {
            [HarmonyPrefix]
            static bool Prefix(TextWriterManager __instance)
            {
                if (!isEditingLabel) return true;

                InputField field = Traverse.Create(__instance).Field("inputField").GetValue<InputField>();
                string enteredText = field != null ? (field.text ?? "") : "";

                if (!Labels.ContainsKey(pendingStorageIndex))
                    Labels.Add(pendingStorageIndex, enteredText);
                else
                    Labels[pendingStorageIndex] = enteredText;

                SaveLabels();
                BroadcastLabelUpdate(pendingStorageIndex, enteredText);

                CloseSession();
                return false;
            }
        }

        [HarmonyPatch(typeof(TextWriterManager), "AbortTyping")]
        internal static class Patch_AbortTyping
        {
            [HarmonyPrefix]
            static bool Prefix()
            {
                if (!isEditingLabel) return true;

                CloseSession();
                return false;
            }
        }
    }

    internal class LabelWatcher : MonoBehaviour
    {
        private void Update()
        {
            ProcessIncomingNetworkMessages();

            KeyCode keyMain = ExtraSettingsAPI_GetKeybindMain("storageLabelKeybind") == KeyCode.None ? KeyCode.F1 : ExtraSettingsAPI_GetKeybindMain("storageLabelKeybind");
            KeyCode keyAlt = ExtraSettingsAPI_GetKeybindAlt("storageLabelKeybind");
            bool keyUpMain = Input.GetKeyUp(keyMain);
            bool keyUpAlt = Input.GetKeyUp(keyAlt);

            if (self == null || !(keyUpMain || keyUpAlt) || Patch_Storage_Small.storageInstance == null || !Patch_Storage_Small.canOpenLabelMenu)
                return;

            uint storageIndex = Patch_Storage_Small.storageInstance.ObjectIndex;
            string currentLabel = Labels.ContainsKey(storageIndex) ? Labels[storageIndex] : "";
            LabelTextWriter.Open(storageIndex, currentLabel);
        }
    }
}
