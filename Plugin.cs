using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TerminalChecker
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class TerminalCheckerPlugin : BaseUnityPlugin
    {
        internal const string ModName = "TerminalChecker";
        internal const string ModVersion = "2.0.1";
        internal const string Author = "Azumatt";
        private const string ModGUID = Author + "." + ModName;
        private readonly Harmony _harmony = new(ModGUID);
        public static readonly ManualLogSource TerminalCheckerLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);
        private Coroutine? _modcheckerCoroutine;
        internal static Dictionary<string, PreparedPackageInfo>? allPreparedPackagesInfo = null;
        internal static bool CanCompareMods = false;

        public void Awake()
        {
            _modcheckerCoroutine = StartCoroutine(Utilities.CheckModUpdates());
            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
        }

        public void Start()
        {
            if (CanCompareMods)
            {
                TerminalCheckerLogger.LogDebug("Comparing local mods to prepared packages");
                Utilities.CompareLocalModsToPreparedPackage();
            }
            else
            {
                TerminalCheckerLogger.LogWarning("CanCompareMods is false, not comparing local mods to prepared packages");

                // Check again in 5 seconds
                Invoke(nameof(Utilities.CompareLocalModsToPreparedPackage), 10f);
            }
        }

        private void OnDestroy()
        {
            if (_modcheckerCoroutine != null)
            {
                StopCoroutine(_modcheckerCoroutine);
            }
        }
    }
}