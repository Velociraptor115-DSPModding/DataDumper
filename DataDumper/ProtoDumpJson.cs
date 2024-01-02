using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DysonSphereProgram.Modding.DataDumper;


public class ProtoDumpJson
{
  public static void Execute()
  {
    var jsonDumpDir = "/JsonDump";
    if (!Directory.Exists(jsonDumpDir)) Directory.CreateDirectory(jsonDumpDir);

    var serializerOpts = new JsonSerializerOptions()
    {
      WriteIndented = true,
      TypeInfoResolverChain = { CustomJsonSerializerContext.Default }
    };

    var customSerializerConfig = File.ReadAllText($"{jsonDumpDir}/config.json");
    var customSerializers = JsonSerializer.Deserialize<Dictionary<string, CustomJsonSerializer>>(customSerializerConfig, serializerOpts);

    foreach (var serializerConfigKvp in customSerializers)
    {
      using var stream = File.Open($"{jsonDumpDir}/{serializerConfigKvp.Key}.json", FileMode.Create, FileAccess.Write);
      serializerConfigKvp.Value.Serialize(stream, new JsonWriterOptions()
      {
        Indented = true
      });
    }

    // var analysedTypes = TypeSerializationDescription.AnalyseTypes(typeof(TechProto));
    // var analysedTypesSerialized = JsonSerializer.Serialize(analysedTypes, serializerOpts);
    // File.WriteAllText($"{jsonDumpDir}/analysedTypes-tech.json", analysedTypesSerialized);
  }
}
