using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DysonSphereProgram.Modding.DataDumper;

public class IconRip
{
  private static List<string> failedIcons = new List<string>();

  private static void SaveAsPng(Sprite sprite, string path)
  {
    if (sprite == null)
    {
      Plugin.Logger.LogWarning($"Sprite is null for {path}");
      return;
    }
    try
    {
      File.WriteAllBytes(path, TextureRip.GetPngBytes(sprite));
    }
    catch (Exception e)
    {
      failedIcons.Add(path);
      Plugin.Logger.LogWarning($"Couldn't read texture for {path}");
    }
  }

  private static string CustomCombine(string v1, string v2) => $"{v1}_{v2}";
  private static string CustomCombineDir(string v1, string v2) => $"{v1}/{v2}";

  public static void Execute()
  {
    if (!Directory.Exists("/IconRip")) Directory.CreateDirectory("/IconRip");
    DumpProto("/IconRip/items", LDB.items, (proto, path) =>
    {
      SaveAsPng(proto.iconSprite, CustomCombine(path, "icon.png"));
    });

    DumpProto("/IconRip/recipes", LDB.recipes, (proto, path) =>
    {
      SaveAsPng(proto.iconSprite, CustomCombine(path, "icon.png"));
    });

    DumpProto("/IconRip/techs", LDB.techs, (proto, path) =>
    {
      SaveAsPng(proto.iconSprite, CustomCombine(path, "icon.png"));
    });

    DumpProto("/IconRip/enemies", LDB.enemies, (proto, path) =>
    {
      SaveAsPng(proto.iconSprite, CustomCombine(path, "icon.png"));
    });

    DumpProto("/IconRip/fleets", LDB.fleets, (proto, path) =>
    {
      SaveAsPng(proto.iconSprite, CustomCombine(path, "icon.png"));
    });

    DumpProto("/IconRip/veges", LDB.veges, (proto, path) =>
    {
      SaveAsPng(proto.iconSprite, CustomCombine(path, "icon.png"));
    });

    DumpProto("/IconRip/veins", LDB.veins, (proto, path) =>
    {
      SaveAsPng(proto.iconSprite, CustomCombine(path, "icon.png"));
    });

    DumpProto("/IconRip/milestones", LDB.milestones, (proto, path) =>
    {
      SaveAsPng(proto.iconSprite, CustomCombine(path, "icon.png"));
    });

    DumpProto("/IconRip/achievements", LDB.achievements, (proto, path) =>
    {
      SaveAsPng(proto.iconSprite, CustomCombine(path, "icon.png"));
    });

    DumpProto("/IconRip/signals", LDB.signals, (proto, path) =>
    {
      SaveAsPng(proto.iconSprite, CustomCombine(path, "icon.png"));
    });

    File.WriteAllText("/IconRip/failedIcons.txt", string.Join("\n", failedIcons));
  }

  private static void DumpProto<T>(string protosPath, ProtoSet<T> protoSet, Action<T, string> action) where T : Proto
  {
    if (!Directory.Exists(protosPath)) Directory.CreateDirectory(protosPath);
    foreach (var proto in protoSet.dataArray)
    {
      var protoPath = CustomCombineDir(protosPath, proto.name.All(x => x < sbyte.MaxValue) ? $"{proto.ID}_{proto.name}" : $"{proto.ID}");
      protoPath = protoPath.Replace(":", "_").Replace("?", "");
      try
      {
        action(proto, protoPath);
      }
      catch (Exception e)
      {
        Plugin.Logger.LogWarning($"Couldn't dump proto {proto.ID} in {protosPath}");
      }
    }
  }
}
