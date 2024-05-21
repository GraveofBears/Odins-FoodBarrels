using System.IO;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using PieceManager;
using ServerSync;
using LocalizationManager;
using BepInEx.Logging;

namespace OdinsFoodBarrels
{
    [BepInPlugin(HGUIDLower, ModName, ModVersion)]
    public class OdinsFoodBarrelsPlugin : BaseUnityPlugin
    {
        public const string ModVersion = "1.0.21";
        public const string ModName = "OdinsFoodBarrels";
        internal const string Author = "Gravebear";
        internal const string HGUID = Author + "." + "OdinsFoodBarrels";
        internal const string HGUIDLower = "gravebear.odinsfoodbarrels";
        private const string ModGUID = "Harmony." + Author + "." + ModName;
        private static readonly string ConfigFileName = HGUIDLower + ".cfg";
        private static readonly string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
        public static string ConnectionError = "";

        private static readonly ConfigSync configSync = new(ModName) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

        private static ConfigEntry<Toggle> serverConfigLocked = null!;

        private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
        {
            ConfigEntry<T> configEntry = Config.Bind(group, name, value, description);

            SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) => config(group, name, value, new ConfigDescription(description), synchronizedSetting);

        private enum Toggle
        {
            On = 1,
            Off = 0
        }

        // Create logger for debug messaging of RestrictContainers
        internal static ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource(ModName);

        private const string assetBundle = "odinsnummies";
        private static readonly Dictionary<string, BuildPiece> BuildPieces = new();
        private static readonly Dictionary<string, HashSet<string>> ContainerRestrictions = new();

        internal static Localization english = null!;


        private void Awake()
        {
            Localizer.Load();

            CreateBuildPiece(assetBundle, "OH_Raspberries", "Raspberry", 10, true, BuildPieceCategory.Misc);

            CreateBuildPiece(assetBundle, "OH_Blue_Mushrooms", "MushroomBlue", 10, true, BuildPieceCategory.Misc);

            CreateBuildPiece(assetBundle, "OH_Blueberries", "Blueberries", 10, true, BuildPieceCategory.Misc);

            CreateBuildPiece(assetBundle, "OH_Carrots", "Carrot", 10, true, BuildPieceCategory.Misc);

            CreateBuildPiece(assetBundle, "OH_CloudBerries", "Cloudberry", 10, true, BuildPieceCategory.Misc);

            CreateBuildPiece(assetBundle, "OH_Fish", "FishRaw", 10, true, BuildPieceCategory.Misc);

            CreateBuildPiece(assetBundle, "OH_Honey", "Honey", 10, true, BuildPieceCategory.Misc);

            CreateBuildPiece(assetBundle, "OH_Red_Mushrooms", "Mushroom", 10, true, BuildPieceCategory.Misc);

            CreateBuildPiece(assetBundle, "OH_Turnips", "Turnip", 10, true, BuildPieceCategory.Misc);

            CreateBuildPiece(assetBundle, "OH_Yellow_Mushrooms", "MushroomYellow", 10, true, BuildPieceCategory.Misc);

            CreateBuildPiece(assetBundle, "OH_Thistle", "Thistle", 10, true, BuildPieceCategory.Misc);

            CreateBuildPiece(assetBundle, "OH_Dandelion", "Dandelion", 10, true, BuildPieceCategory.Misc);

            CreateBuildPiece(assetBundle, "OH_Barley", "Barley", 10, true, BuildPieceCategory.Misc);

            CreateBuildPiece(assetBundle, "OH_Flax", "Flax", 10, true, BuildPieceCategory.Misc);

            CreateBuildPiece(assetBundle, "OH_Onions", "Onion", 10, true, BuildPieceCategory.Misc);

            CreateBuildPiece(assetBundle, "OH_Egg_Basket", "ChickenEgg", 10, true, BuildPieceCategory.Misc);

            CreateBuildPiece(assetBundle, "OH_JotunPuffs_Basket", "MushroomJotunPuffs", 10, true, BuildPieceCategory.Misc);

            CreateBuildPiece(assetBundle, "OH_Magecap", "MushroomMagecap", 10, true, BuildPieceCategory.Misc);

            CreateBuildPiece(assetBundle, "OH_RoyalJelly", "RoyalJelly", 10, true, BuildPieceCategory.Misc);

            CreateBuildPiece(assetBundle, "OH_Sap_Barrel", "Sap", 10, true, BuildPieceCategory.Misc);

            CreateBuildPiece(assetBundle, "OH_Seedbag", "DeerHide", 5, true, BuildPieceCategory.Misc);

            // override the allowable items for OH_Seedbag since we want to it be seeds and not DeerHide
            ContainerRestrictions["$OH_Seedbag"] = new HashSet<string>()
            {
                "Acorn",
                "AncientSeed",
                "BeechSeeds",
                "BirchSeeds",
                "CarrotSeeds",
                "OnionSeeds",
                "TurnipSeeds",
                "FirCone",
                "PineCone",
                "VineberrySeeds",
                "AncientSeed",
            };

            // Set up restrictions for containers
            RestrictContainers.SetContainerRestrictions(ContainerRestrictions);

            Assembly assembly = Assembly.GetExecutingAssembly();
            Harmony harmony = new(ModGUID);
            harmony.PatchAll(assembly);
        }

        /// <summary>
        ///     Method to create food barrels and add values to container restrictions
        /// </summary>
        /// <param name="assetBundleFileName"></param>
        /// <param name="prefabName"></param>
        /// <param name="requiredItem"></param>
        /// <param name="itemAmount"></param>
        /// <param name="recover"></param>
        /// <param name="category"></param>
        private static void CreateBuildPiece(
            string assetBundleFileName,
            string prefabName,
            string requiredItem,
            int itemAmount,
            bool recover,
            BuildPieceCategory category
        )
        {
            BuildPiece buildPiece = new(assetBundleFileName, prefabName);
            buildPiece.RequiredItems.Add(requiredItem, itemAmount, recover);
            buildPiece.Category.Set(category);
            BuildPieces.Add(prefabName, buildPiece); // keep a reference to BuildPiece in case PieceManager needs it

            // add tokenized version of prefabName, which is also the name for
            // it's container component also add the allowable item for this container
            ContainerRestrictions.Add($"${prefabName}", new HashSet<string>() { requiredItem });
        }
    }
}
