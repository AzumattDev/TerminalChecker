using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TerminalChecker
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class TerminalCheckerPlugin : BaseUnityPlugin
    {
        internal const string ModName = "TerminalChecker";
        internal const string ModVersion = "1.0.0";
        internal const string Author = "Azumatt";
        private const string ModGUID = Author + "." + ModName;

        private readonly Harmony _harmony = new(ModGUID);

        public static readonly ManualLogSource TerminalCheckerLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);

        public void Awake()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
        }

        public void Start()
        {
            DetectModsPatchingTerminal();
        }

        public static void DetectModsPatchingTerminal()
        {
            var allPatchedMethods = Harmony.GetAllPatchedMethods();
            var comparisonDateEastern = new DateTime(2023, 8, 22, 8, 30, 0); // The time very shortly after the Hildir Update dropped
            var easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var comparisonDateUtc = TimeZoneInfo.ConvertTimeToUtc(comparisonDateEastern, easternZone);

            LogTheThings($"Mods that patch Terminal class and appear to be older than the Hildir Update:{Environment.NewLine}", true);
            foreach (var method in allPatchedMethods.Where(m => m.DeclaringType == typeof(Terminal)))
            {
                var patches = Harmony.GetPatchInfo(method);
                CheckPatchType(patches.Prefixes, "Prefix", method.Name, comparisonDateUtc);
                CheckPatchType(patches.Postfixes, "Postfix", method.Name, comparisonDateUtc);
                CheckPatchType(patches.Transpilers, "Transpiler", method.Name, comparisonDateUtc);
            }
        }

        private static void CheckPatchType(IEnumerable<Patch> patchList, string patchTypeName, string methodName, DateTime comparisonDate)
        {
            foreach (var patch in patchList)
            {
                if (patch.PatchMethod.DeclaringType == null) continue;

                var assemblyDate = GetLinkerTime(patch.PatchMethod.DeclaringType.Assembly, TimeZoneInfo.Utc);
                if (assemblyDate >= comparisonDate) continue;

#if DEBUG
                var timeDifference = comparisonDate - assemblyDate;
                LogTheThings($"Assembly `{patch.PatchMethod.DeclaringType.Assembly.GetName().Name}` appears to be old. " +
                             $"Detected UTC timestamp: {assemblyDate:G} which is {timeDifference.Days} days, {timeDifference.Hours} hours, and {timeDifference.Minutes} minutes older than reference date." +
                             $" It has a {patchTypeName} patch on method {methodName} in Terminal class. Inside the following patch: {patch.PatchMethod.Name}{Environment.NewLine}");

#else
                LogTheThings($"Assembly `{patch.PatchMethod.DeclaringType.Assembly.GetName().Name}` appears to be old and has a " +
                             $"{patchTypeName} patch on method {methodName} in Terminal class. Inside the following patch: {patch.PatchMethod.Name}{Environment.NewLine}");
#endif
            }
        }

        public static DateTime GetLinkerTime(Assembly assembly, TimeZoneInfo? target = null)
        {
            var filePath = assembly.Location;
            const int c_PeHeaderOffset = 60;
            const int c_LinkerTimestampOffset = 8;

            var buffer = new byte[2048];

            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                stream.Read(buffer, 0, 2048);

            var offset = BitConverter.ToInt32(buffer, c_PeHeaderOffset);
            var secondsSince1970 = BitConverter.ToInt32(buffer, offset + c_LinkerTimestampOffset);
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var linkTimeUtc = epoch.AddSeconds(secondsSince1970);

            var tz = target ?? TimeZoneInfo.Local;
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(linkTimeUtc, tz);

            return localTime;
        }

        private static void LogTheThings(string message, bool isHeader = false)
        {
            if (Application.platform == RuntimePlatform.WindowsPlayer)
            {
                ConsoleManager.SetConsoleColor(ConsoleColor.DarkCyan);
                ConsoleManager.ConsoleStream.WriteLine($"[Debug  :{ModName}] {message}");
                LogToDisk($"[Debug  :{ModName}] {message}");
                ConsoleManager.SetConsoleColor(ConsoleColor.White);
            }
            else
            {
                TerminalCheckerLogger.LogWarning(message);
            }
        }

        private static void LogToDisk(string message)
        {
            foreach (ILogListener logListener in BepInEx.Logging.Logger.Listeners)
            {
                if (logListener is DiskLogListener { LogWriter: not null } bepinexlog)
                {
                    bepinexlog.LogWriter.WriteLine(message);
                }
            }
        }
    }
}