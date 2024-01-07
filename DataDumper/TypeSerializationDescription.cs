using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using UnityEngine;

namespace DysonSphereProgram.Modding.DataDumper;

public class TypeSerializationDescription
{
  public Dictionary<string, string> InstanceFields { get; set; }
  public Dictionary<string, string> InstanceProperties { get; set; }
  public Dictionary<string, string> StaticFields { get; set; }
  public Dictionary<string, string> StaticProperties { get; set; }

  public static TypeSerializationDescription DescribeType(Type type)
  {
    // Plugin.Log.LogInfo(type.FullName);
    var bFlags = BindingFlags.Public | BindingFlags.NonPublic;
    // Plugin.Log.LogInfo("instance fields");
    var instanceFields = type.GetFields(bFlags | BindingFlags.Instance)
      .ToDictionary(f => f.Name, f => f.FieldType.FullName);
    // Plugin.Log.LogInfo("static fields");
    var staticFields = type.GetFields(bFlags | BindingFlags.Static)
      .ToDictionary(f => f.Name, f => f.FieldType.FullName);
    
    // Plugin.Log.LogInfo("instance properties");
    var instanceProperties = type.GetProperties(bFlags | BindingFlags.Instance)
      .ToDictionary(f => $"{f.Name}{string.Join(",", f.GetIndexParameters().Select(x => x.ParameterType.FullName))}", f => f.PropertyType.FullName);
    // Plugin.Log.LogInfo("static properties");
    var staticProperties = type.GetProperties(bFlags | BindingFlags.Static)
      .ToDictionary(f => f.Name, f => f.PropertyType.FullName);

    return new TypeSerializationDescription()
    {
      InstanceFields = instanceFields,
      StaticFields = staticFields,
      InstanceProperties = instanceProperties,
      StaticProperties = staticProperties
    };
  }

  public static bool IsTrivialType(Type type)
  {
    if (type.IsPrimitive || type.IsEnum || type == typeof(string))
      return true;
    return false;
  }
  
  public static Type? NormalizeInterestingType(Type? type)
  {
    if (type == null)
      return type;
    if (IsTrivialType(type))
      return type;
    if (type.IsArray)
      return NormalizeInterestingType(type.GetElementType());

    var iListInterfaces = type.FindInterfaces((x, y) => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IList<>), null);
    if (iListInterfaces.Length > 0)
    {
      // Plugin.Logger.LogInfo($"Found IList interface(s) on {type.FullName} - {string.Join(",", iListInterfaces.Select(x => x.FullName))}");
      return NormalizeInterestingType(iListInterfaces[0].GenericTypeArguments[0]);
    }
    return type;
  }
  
  public static IEnumerator AnalyseTypesCoroutine(Type rootType)
  {
    HashSet<Type> expandedTypes = new();
    Queue<Type> typesToExpandQueue = new();
    
    typesToExpandQueue.Enqueue(rootType);

    while (typesToExpandQueue.Count > 0)
    {
      var typeToExpand = typesToExpandQueue.Dequeue();
      Plugin.Logger.LogInfo($"Expanding {typeToExpand.FullName}");
      expandedTypes.Add(typeToExpand);
      var bFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
      var fields = typeToExpand.GetFields(bFlags);
      foreach (var field in fields)
      {
        Plugin.Logger.LogInfo($"{field.FieldType.FullName} {field.Name}");
      }
      var properties = typeToExpand.GetProperties(bFlags);
      foreach (var property in properties)
      {
        Plugin.Logger.LogInfo($"{property.PropertyType.FullName} {property.Name}");
      }
      
      var typesToExpandSet = new HashSet<Type>();
      typesToExpandSet.UnionWith(fields.Select(x => NormalizeInterestingType(x.FieldType)).Where(x => x != null).Cast<Type>());
      typesToExpandSet.UnionWith(properties.Select(x => NormalizeInterestingType(x.PropertyType)).Where(x => x != null).Cast<Type>());
      typesToExpandSet.ExceptWith(expandedTypes);
      typesToExpandSet.ExceptWith(typesToExpandQueue);

      // typesToExpandSet.RemoveWhere(IsTrivialType);
      // typesToExpandSet.RemoveWhere(x => x.IsConstructedGenericType);
      
      Plugin.Logger.LogInfo($"Types to expand: {string.Join("\n", typesToExpandSet.Select(x => x.FullName))}");

      while (true)
      {
        if (Input.GetKey(KeyCode.P))
          break;
        yield return null;
      }

      yield return new WaitForSeconds(1);

      foreach (var type in typesToExpandSet)
      {
        typesToExpandQueue.Enqueue(type);
      }
    }
  }

  public static Dictionary<string, TypeSerializationDescription> AnalyseTypes(Type rootType)
  {
    HashSet<Type> expandedTypes = new();
    Queue<Type> typesToExpandQueue = new();
    
    typesToExpandQueue.Enqueue(rootType);

    while (typesToExpandQueue.Count > 0)
    {
      var typeToExpand = typesToExpandQueue.Dequeue();
      expandedTypes.Add(typeToExpand);
      var bFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance; // | BindingFlags.Static;
      var fieldTypes = typeToExpand.GetFields(bFlags).Select(x => x.FieldType);
      var propertyTypes = typeToExpand.GetProperties(bFlags).Select(x => x.PropertyType);
      
      var typesToExpandSet = new HashSet<Type>();
      typesToExpandSet.UnionWith(fieldTypes);
      typesToExpandSet.UnionWith(propertyTypes);
      typesToExpandSet.ExceptWith(expandedTypes);
      typesToExpandSet.ExceptWith(typesToExpandQueue);

      typesToExpandSet.RemoveWhere(IsTrivialType);
      typesToExpandSet.RemoveWhere(x => x.IsConstructedGenericType);

      foreach (var type in typesToExpandSet)
      {
        typesToExpandQueue.Enqueue(type);
      }
    }

    Plugin.Logger.LogInfo(expandedTypes.Count);
    var dict = new Dictionary<string, TypeSerializationDescription>();
    foreach (var type in expandedTypes)
    {
      if (dict.ContainsKey(type.FullName))
      {
        Plugin.Logger.LogInfo($"Already exists: {type.FullName}");
        continue;
      }
      dict.Add(type.FullName, DescribeType(type));
    }

    return dict;
  }
}
