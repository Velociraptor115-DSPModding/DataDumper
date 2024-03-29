using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace DysonSphereProgram.Modding.DataDumper;

using static MyPluginInfo;

[BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
  internal new static ManualLogSource Logger;
  internal static ConfigEntry<string> DumpPath { get; private set; }
  
  private void Awake()
  {
    // Plugin startup logic
    Logger = base.Logger;
    Logger.LogInfo($"Plugin {PLUGIN_GUID} is loaded!");

    DumpPath = Config.Bind("DataDumper", "DumpPath", "/JsonDump");
    
    ProtoDumpJson.Execute();
  }

  private void OnDestroy()
  {
    // Plugin cleanup logic
    Logger.LogInfo($"Plugin {PLUGIN_GUID} is unloaded!");
  }
}
