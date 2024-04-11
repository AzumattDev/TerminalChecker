using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using YamlDotNet.Serialization;

namespace TerminalChecker;

public class PreparedPackageInfo
{
    public string? name { get; set; }
    public string? clean_name { get; set; }
    public string? icon_url { get; set; }
    public string? version { get; set; }
    public string? updated { get; set; }
    public bool? deprecated { get; set; }
    public string[]? urls { get; set; }
}

public class Utilities
{
    internal static IEnumerator CheckModUpdates()
    {
        if (TerminalCheckerPlugin.allPreparedPackagesInfo == null)
        {
            yield return GetAllModsModVersionCheckBot((result) =>
            {
                if (result != null)
                {
                    TerminalCheckerPlugin.TerminalCheckerLogger.LogDebug("Got prepared mods from mod-version-check.eu");
                    TerminalCheckerPlugin.CanCompareMods = true;
                }
                else
                {
                    TerminalCheckerPlugin.TerminalCheckerLogger.LogError("Error: Could not get prepared mods from mod-version-check.eu");
                }
            });
        }
        else
        {
            CompareLocalModsToPreparedPackage();
        }
    }

    private static IEnumerator GetAllModsModVersionCheckBot(System.Action<Dictionary<string, PreparedPackageInfo>?> callback)
    {
        string url = $"https://mod-version-check.eu/api/experimental/prepared-mods";
        using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
        {
            webRequest.SetRequestHeader("Accept-Encoding", "gzip");
            yield return webRequest.SendWebRequest();

            if (webRequest.result is UnityWebRequest.Result.ConnectionError or UnityWebRequest.Result.ProtocolError)
            {
                TerminalCheckerPlugin.TerminalCheckerLogger.LogError(webRequest.error);
                callback(null);
            }
            else
            {
                string jsonData = webRequest.downloadHandler.text;

                IDeserializer deserializer = new DeserializerBuilder().Build();
                TerminalCheckerPlugin.allPreparedPackagesInfo = deserializer.Deserialize<Dictionary<string, PreparedPackageInfo>>(jsonData);
                callback(TerminalCheckerPlugin.allPreparedPackagesInfo);
            }
        }
    }

    internal static void CompareLocalModsToPreparedPackage()
    {
        Dictionary<string, PluginInfo>? plugins = Chainloader.PluginInfos;
        foreach (PluginInfo? pluginInfo in plugins.Values)
        {
            // Find the mod using the converted name.
            KeyValuePair<string, PreparedPackageInfo>? matchedMod = TerminalCheckerPlugin.allPreparedPackagesInfo?.FirstOrDefault(x => x.Value.clean_name != null && x.Value.clean_name.Equals(pluginInfo.Metadata.Name.ToLower(), StringComparison.OrdinalIgnoreCase));
            // If the mod is found and has versions, get the latest one.
            if (matchedMod?.Value is not { version.Length: > 0 }) continue;
            string? latestVersion = matchedMod.Value.Value.version; // Assuming versions are sorted with latest first.
            Assembly? assem = pluginInfo.Instance?.GetType().Assembly;
            if (assem == null) continue;
            CompareAndPrint(pluginInfo.Metadata.Name, assem, pluginInfo.Metadata.Version.ToString(), latestVersion, matchedMod);
        }
    }

    private static void CompareAndPrint(string modName, Assembly assem, string? localVersion, string? latestVersion, KeyValuePair<string, PreparedPackageInfo>? packageInfo)
    {
        System.Version localVer = ParseVersion(localVersion);
        System.Version onlineVer = ParseVersion(latestVersion);
        var assemName = assem.GetName().Name;
        DateTime modUtcDateTime = DateTime.Parse(packageInfo?.Value.updated, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        IEnumerable<MethodBase> allPatchedMethods = Harmony.GetAllPatchedMethods().Where(m => m.DeclaringType == typeof(Terminal));
        DateTime comparisonDateEasternHildir = new DateTime(2023, 8, 22, 8, 30, 0);
        TimeZoneInfo? easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        DateTime comparisonDateUtcHildir = TimeZoneInfo.ConvertTimeToUtc(comparisonDateEasternHildir, easternZone);

        DateTime comparisonDateEastern021746 = new DateTime(2024, 4, 10, 8, 0, 0);
        DateTime comparisonDateUtc021746 = TimeZoneInfo.ConvertTimeToUtc(comparisonDateEastern021746, easternZone);

        var patchDetails = new List<string>();
        foreach (MethodBase? method in allPatchedMethods)
        {
            Patches? patches = Harmony.GetPatchInfo(method);
            patchDetails.AddRange(CollectPatchDetails(patches.Prefixes, "Prefix", method.Name, assemName));
            patchDetails.AddRange(CollectPatchDetails(patches.Postfixes, "Postfix", method.Name, assemName));
            patchDetails.AddRange(CollectPatchDetails(patches.Transpilers, "Transpiler", method.Name, assemName));
        }

        if (patchDetails.Any())
        {
            PrintModUpdateDetails(assemName, modUtcDateTime, comparisonDateUtcHildir, comparisonDateUtc021746, patchDetails);
        }
    }

    private static IEnumerable<string> CollectPatchDetails(IEnumerable<Patch> patchList, string patchTypeName, string methodName, string assemName)
    {
        return patchList
            .Where(patch => patch.PatchMethod.DeclaringType?.Assembly.GetName().Name == assemName)
            .Select(patch => $"{patchTypeName} patch on {methodName}, inside method: {patch.PatchMethod.Name}");
    }

    private static void PrintModUpdateDetails(string assemName, DateTime modUtcDateTime, DateTime comparisonDateUtcHildir, DateTime comparisonDateUtc021746, List<string> patchDetails)
    {
        bool isNewerThanHildir = modUtcDateTime >= comparisonDateUtcHildir;
        bool isOlderThan021746 = modUtcDateTime < comparisonDateUtc021746;
        if (!isOlderThan021746) return;
        string update = isNewerThanHildir ? "0.217.46 update" : "Hildir update";
        DateTime comparisonDate = isNewerThanHildir ? comparisonDateUtc021746 : comparisonDateUtcHildir;

        TimeSpan timeDifference = comparisonDate - modUtcDateTime;
        Utilities.LogTheThings($"Assembly `{assemName}` is older than the {update}." +
                               $" Detected UTC timestamp: {modUtcDateTime:G} which is {timeDifference.Days} days, {timeDifference.Hours} hours, and {timeDifference.Minutes} minutes older than reference date." +
                               $" Patch details:{Environment.NewLine}{string.Join(Environment.NewLine, patchDetails)}{Environment.NewLine}");
    }

    internal static System.Version ParseVersion(string? input)
    {
        try
        {
            if (input != null)
            {
                System.Version ver = System.Version.Parse(input);
                return ver;
            }
        }
        catch (ArgumentNullException)
        {
            TerminalCheckerPlugin.TerminalCheckerLogger.LogError("Error: String to be parsed is null.");
        }
        catch (ArgumentOutOfRangeException)
        {
            TerminalCheckerPlugin.TerminalCheckerLogger.LogError($"Error: Negative value in '{input}'.");
        }
        catch (ArgumentException)
        {
            TerminalCheckerPlugin.TerminalCheckerLogger.LogError($"Error: Bad number of components in '{input}'.");
        }
        catch (FormatException)
        {
            TerminalCheckerPlugin.TerminalCheckerLogger.LogError($"Error: Non-integer value in '{input}'.");
        }
        catch (OverflowException)
        {
            TerminalCheckerPlugin.TerminalCheckerLogger.LogError($"Error: Number out of range in '{input}'.");
        }

        return System.Version.Parse(TerminalCheckerPlugin.ModVersion);
    }

    internal static void LogTheThings(string message, bool isHeader = false)
    {
        TerminalCheckerPlugin.TerminalCheckerLogger.LogWarning(message);
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