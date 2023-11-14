using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using RaftModLoader;
using HMLLibrary;
using Steamworks;
using HarmonyLib;

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

    public void OnModUnload()
    {
        harmony.UnpatchAll(harmony.Id);
        labelsLoaded = false;
        Debug.Log(prefix + "Mod has been unloaded!");
    }

    public override void WorldEvent_WorldUnloaded()
    {
        Labels = new Dictionary<uint, string>();
        labelsLoaded = false;
    }
    public override void WorldEvent_WorldLoaded()
    {
        LoadLabels();
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
                        string tmp = Helper.GetTerm(ps.plant.pickupComponent.pickupTerm).Split('@')[0];

                        if (tmp.ToLower().Trim().Equals("item")) tmp = "Tree";

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
                        string tmp = Helper.GetTerm(ps.plant.pickupComponent.pickupTerm).Split('@')[0];

                        if (tmp.ToLower().Trim().Equals("item")) tmp = "Tree";

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
        return prefix + "Label '" + string.Join(" ", args) + "' added";
    }

    [ConsoleCommand(name: "deletelabels", docs: "Syntax: deletelabels - Delete all labels from the world")]
    public static string DeleteAllLabels(string[] args)
    {
        if (!LoadSceneManager.IsGameSceneLoaded) return prefix + "You aren't in a world";
        if (ExtraSettingsAPI_Loaded && !ExtraSettingsAPI_GetCheckboxState("enableStorageLabels")) return prefix + "Storage Labels is disabled is this world!";

        Labels = new Dictionary<uint, string>();
        SaveLabels();
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
                if (!labelsLoaded && (Labels.Keys.Count == 0 || Labels.Values.Join(null, "").IsNullOrEmpty())) LoadLabels();

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
            else if (!___showingText || (___showingText && (RAPI.GetLocalPlayer().Inventory.GetSelectedHotbarItem() == null || !RAPI.GetLocalPlayer().Inventory.GetSelectedHotbarItem().UniqueName.ToLower().Contains("water"))))
            {
                string styleFormat = ExtraSettingsAPI_Loaded ? ExtraSettingsAPI_GetComboboxSelectedItem("cropplotPlantListStyle") : "grouped";
                string timeFormat = ExtraSettingsAPI_Loaded ? ExtraSettingsAPI_GetComboboxSelectedItem("cropplotPlantListTimeStyle") : "closer";
                string formattedStr = FormatCropplotPlantList(styleFormat, timeFormat, __instance);

                ___canvas.displayTextManager.ShowText(formattedStr, 0, false, 0);
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

    internal class LabelCreator : MonoBehaviour
    {
        private string input;
        private uint storageIndex;
        private Rect submitWindow;

        public void Awake()
        {
            int num1 = 400;
            int num2 = 200;
            submitWindow = new Rect(Screen.width / 2 - num1 / 2, Screen.height / 2 - num2 / 2 + 50, num1, num2);
        }

        private void OnEnable()
        {
            if (Patch_Storage_Small.storageInstance == null) return;
            storageIndex = Patch_Storage_Small.storageInstance.ObjectIndex;
            Helper.SetCursorVisibleAndLockState(true, 0);
        }

        private void OnDisable()
        {
            input = "";
            storageIndex = 0U;
            Helper.SetCursorVisibleAndLockState(false, (CursorLockMode)1);
        }

        private void OnGUI()
        {
            GUI.backgroundColor = Color.black;
            GUILayout.BeginArea(submitWindow);
            GUILayout.BeginVertical(Array.Empty<GUILayoutOption>());
            GUILayout.BeginHorizontal(Array.Empty<GUILayoutOption>());
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            input = GUILayout.TextField(input, Array.Empty<GUILayoutOption>());
            GUILayout.BeginHorizontal(Array.Empty<GUILayoutOption>());
            if (GUILayout.Button("Cancel", Array.Empty<GUILayoutOption>()))
                enabled = false;

            bool enter = Input.GetKeyUp(KeyCode.KeypadEnter);
            if (GUILayout.Button("Submit", Array.Empty<GUILayoutOption>()) || enter)
            {
                if (!Labels.ContainsKey(storageIndex))
                {
                    Labels.Add(storageIndex, input);
                }
                else Labels[storageIndex] = input;

                enabled = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

    }

    internal class LabelWatcher : MonoBehaviour
    {
        private LabelCreator labelCreator;

        private void Start()
        {
            labelCreator = gameObject.AddComponent<LabelCreator>();
            labelCreator.enabled = false;
        }

        private void Update()
        {
            KeyCode keyMain = ExtraSettingsAPI_GetKeybindMain("storageLabelKeybind") == KeyCode.None ? KeyCode.F1 : ExtraSettingsAPI_GetKeybindMain("storageLabelKeybind");
            KeyCode keyAlt = ExtraSettingsAPI_GetKeybindAlt("storageLabelKeybind");
            bool keyUpMain = Input.GetKeyUp(keyMain);
            bool keyUpAlt = Input.GetKeyUp(keyAlt);

            if (self == null || !(keyUpMain || keyUpAlt) || Patch_Storage_Small.storageInstance == null || !Patch_Storage_Small.canOpenLabelMenu)
                return;

            labelCreator.enabled = !labelCreator.enabled;
        }
    }
}