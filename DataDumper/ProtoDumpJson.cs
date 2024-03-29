using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DysonSphereProgram.Modding.DataDumper;


public class ProtoDumpJson
{
  public static void Execute()
  {
    var jsonDumpDir = Plugin.DumpPath.Value;
    if (!Directory.Exists(jsonDumpDir)) Directory.CreateDirectory(jsonDumpDir);

    var serializerOpts = new JsonSerializerOptions()
    {
      WriteIndented = true,
      ReadCommentHandling = JsonCommentHandling.Skip,
      AllowTrailingCommas = true,
      TypeInfoResolverChain = { CustomJsonSerializerContext.Default }
    };
    
    Plugin.Logger.LogInfo(Path.GetFullPath(jsonDumpDir));
    
    var allInOneConfigFile = $"{jsonDumpDir}/config.json";
    var configDir = $"{jsonDumpDir}/config";

    Dictionary<string, CustomJsonSerializer> customSerializers = new();

    if (File.Exists(allInOneConfigFile))
    {
      var aioSerializerConfigs =  JsonSerializer.Deserialize<Dictionary<string, CustomJsonSerializer>>(File.ReadAllText(allInOneConfigFile), serializerOpts);
      foreach (var kvp in aioSerializerConfigs)
      {
        customSerializers.Add(kvp.Key, kvp.Value);
      }
    }
    
    if (Directory.Exists(configDir))
    {
      var configFiles = Directory.GetFiles(configDir, "*.json");
      foreach (var configFile in configFiles)
      {
        var key = Path.GetFileNameWithoutExtension(configFile);
        var serializerConfig = JsonSerializer.Deserialize<CustomJsonSerializer>(File.ReadAllText(configFile), serializerOpts);
        customSerializers.Add(key, serializerConfig);
      }
    }
    
    var dumpFolder = $"{jsonDumpDir}/dumped";
    Directory.CreateDirectory(dumpFolder);

    var ripFolder = $"{dumpFolder}/rips";

    foreach (var serializerConfigKvp in customSerializers)
    {
      using var stream = File.Open($"{dumpFolder}/{serializerConfigKvp.Key}.json", FileMode.Create, FileAccess.Write);
      serializerConfigKvp.Value.Serialize($"{ripFolder}/{serializerConfigKvp.Key}", stream, new JsonWriterOptions()
      {
        Indented = true
      });
    }

    // var analysedTypes = TypeSerializationDescription.AnalyseTypes(typeof(TechProto));
    // var analysedTypesSerialized = JsonSerializer.Serialize(analysedTypes, serializerOpts);
    // File.WriteAllText($"{jsonDumpDir}/analysedTypes-tech.json", analysedTypesSerialized);
  }
}
