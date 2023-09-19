using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;
using Mono.Cecil;

namespace Abyss;

// ReSharper disable once UnusedType.Global
internal static class Initializer
{
    private const string Id = "com.grahamkracker.abyss.initializer";
    private const string Name = "Abyss.Initializer";

    // ReSharper disable once UnusedMember.Global
    public static IEnumerable<string> TargetDLLs { get; } = Array.Empty<string>(); // Needed in order to get recognized as a patcher

    private static ManualLogSource _logger = null!;
    private static Harmony _harmonyInstance = null!;

    // ReSharper disable once UnusedMember.Global
    public static void Patch(AssemblyDefinition assemblyDefinition)
    {
        // Needed in order to get recognized as a patcher
    }

    // ReSharper disable once UnusedMember.Global
    public static void Finish() //called by bie
    {
        _logger = Logger.CreateLogSource(Name);
        _harmonyInstance = new Harmony(Id);
        _harmonyInstance.Patch(typeof(Chainloader).GetMethod(nameof(Chainloader.Initialize)),
            postfix: new HarmonyMethod(typeof(Initializer).GetMethod(nameof(Chainloader_Start))));
    }

    public static void Chainloader_Start() => _harmonyInstance.Patch(typeof(Chainloader).GetMethod(nameof(Chainloader.Start)),
        transpiler: new HarmonyMethod(typeof(Initializer).GetMethod(nameof(FindPluginTypes))));

    public static IEnumerable<CodeInstruction> FindPluginTypes(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var code in instructions)
        {
            if (code.Calls(AccessTools.Method(typeof(TypeLoader), nameof(TypeLoader.FindPluginTypes)).MakeGenericMethod(typeof(PluginInfo))))
            {
                yield return code;
                yield return new CodeInstruction(OpCodes.Call, typeof(Initializer).GetMethod(nameof(TypeLoader_FindPluginTypes)));
            }
            else if (code.Calls(AccessTools.PropertySetter(typeof(PluginInfo), nameof(PluginInfo.Instance))))
            {
                yield return code;

                yield return new CodeInstruction(OpCodes.Ldloc, 23); //pluginInfo
                yield return new CodeInstruction(OpCodes.Call, typeof(Initializer).GetMethod(nameof(PluginInfo_set_Instance)));
            }
            else
            {
                yield return code;
            }
        }
    }

    public static void PluginInfo_set_Instance(PluginInfo pluginInfo)
    {
        var pluginType = pluginInfo.Instance.GetType();

        var initDredgeMod = pluginType.GetMethod("InitDredgeMod", AccessTools.all);

        if (initDredgeMod != null)
        {
            initDredgeMod.Invoke(pluginInfo.Instance, null);
        }
    }


    public static Dictionary<string, List<PluginInfo>> TypeLoader_FindPluginTypes(Dictionary<string, List<PluginInfo>> pluginInfos)
    {
        Dictionary<string, AssemblyNameReference> referencedAbyssAssemblies = new();
        HashSet<string> currentAssemblies = new();
        foreach (var location in pluginInfos.Keys)
        {
            var module = ModuleDefinition.ReadModule(location);
            var assemblyName = Path.GetFileNameWithoutExtension(location);
            _logger.LogInfo($"Scanning {assemblyName} for abyss dependencies to download.");
            currentAssemblies.Add(assemblyName);
            foreach (var assemblyNameReference in module.AssemblyReferences.Where(x=> x.Name.StartsWith("Abyss")))
            {
                if (!referencedAbyssAssemblies.ContainsKey(assemblyNameReference.Name))
                {
                    referencedAbyssAssemblies.Add(assemblyNameReference.Name, assemblyNameReference);
                }
            }
        }

        foreach ((string name, var assembly) in referencedAbyssAssemblies.Select(x => (x.Key, x.Value)).Where(x=> !currentAssemblies.Contains(x.Key)))
        {
            //todo: versioning
            _logger.LogInfo($"{name} is a required dependency and will automatically downloaded.");
            var dllUrl = $"https://github.com/AbyssMod/Abyss/releases/latest/download/{name}.dll";

            using var webClient = new WebClient();

            try
            {
                webClient.DownloadFile(dllUrl, Path.Combine(Paths.PluginPath, $"{name}.dll"));
            }
            catch (Exception e)
            {
                _logger.LogError($"Failed to download {name} from {dllUrl}.");
                _logger.LogError(e);
            }

            var xmlUrl = $"https://github.com/AbyssMod/Abyss/releases/latest/download/{name}.xml";
            try
            {
                webClient.DownloadFile(xmlUrl, Path.Combine(Paths.PluginPath, $"{name}.xml"));
            }
            catch (Exception e)
            {
                _logger.LogError($"Failed to download {name} from {xmlUrl}.");
                _logger.LogError(e);
            }

        }

        return pluginInfos;
    }
}