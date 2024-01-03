using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using UnityEngine;

namespace DysonSphereProgram.Modding.DataDumper;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StringType), typeDiscriminator: "string")]
[JsonDerivedType(typeof(NumberType), typeDiscriminator: "number")]
[JsonDerivedType(typeof(BooleanType), typeDiscriminator: "bool")]
[JsonDerivedType(typeof(ArrayType), typeDiscriminator: "array")]
[JsonDerivedType(typeof(ObjectType), typeDiscriminator: "object")]
public abstract class DataType
{
  public abstract void Write(Utf8JsonWriter writer, object value);
}

public class StringType : DataType
{
  public override void Write(Utf8JsonWriter writer, object value)
  {
    writer.WriteStringValue(Convert.ToString(value));
  }
}

public class NumberType : DataType
{
  public override void Write(Utf8JsonWriter writer, object value)
  {
    writer.WriteNumberValue(Convert.ToDecimal(value));
  }
}

public class BooleanType : DataType
{
  public override void Write(Utf8JsonWriter writer, object value)
  {
    writer.WriteBooleanValue(Convert.ToBoolean(value));
  }
}

public class ArrayType : DataType
{
  public ArrayType(DataType elementType)
  {
    ElementType = elementType;
  }
  
  public DataType ElementType { get; set; }
  public override void Write(Utf8JsonWriter writer, object value)
  {
    var array = value as IList;
    if (array == null)
      throw new Exception("Expected IList");
    
    writer.WriteStartArray();
    foreach (var item in array)
    {
      ElementType.Write(writer, item);
    }
    writer.WriteEndArray();
  }
}

public enum ObjectMemberType
{
  Field,
  Property
}

public struct ObjectMemberReference
{
  public ObjectMemberType Type { get; set; }
  public string Name { get; set; }
  public DataType DataType { get; set; }
  
  private static readonly BindingFlags bFlags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance; 

  public object? GetMember(object obj, bool debug = false)
  {
    return Type switch
    {
      ObjectMemberType.Field => obj.GetType().GetField(Name, bFlags)?.GetValue(obj),
      ObjectMemberType.Property => obj.GetType().GetProperty(Name, bFlags)?.GetValue(obj),
      _ => null
    };
  }
}

public class ObjectType : DataType
{
  public ObjectType(List<ObjectMemberReference> fields)
  {
    Fields = fields;
  }

  public List<ObjectMemberReference> Fields { get; set; }

  public override void Write(Utf8JsonWriter writer, object value)
  {
    writer.WriteStartObject();
    foreach (var field in Fields)
    {
      var memberValue = field.GetMember(value);

      if (memberValue != null)
      {
        writer.WritePropertyName(field.Name);
        field.DataType.Write(writer, memberValue);
      }
    }
    writer.WriteEndObject();
  }
}

public enum CustomSerializerObjectReferenceItemType
{
  Type,
  Field,
  Property
}


public struct CustomSerializerObjectReferenceItem
{
  public CustomSerializerObjectReferenceItemType Type { get; set; }
  public string Name { get; set; }
}

public class CustomJsonSerializer
{
  public List<CustomSerializerObjectReferenceItem> RootObject { get; set; }
  public DataType Schema { get; set; }
  
  public bool UseInference { get; set; }

  private object? GetRootObjectReference()
  {
    var assembly = typeof(LDB).Assembly;
    object typeObj = assembly;
    var enumerator = RootObject.GetEnumerator();
    while (enumerator.MoveNext() && enumerator.Current.Type == CustomSerializerObjectReferenceItemType.Type)
    {
      typeObj = assembly.GetType(enumerator.Current.Name);
    }
    var type = typeObj as Type;
    if (type == null)
      throw new Exception("Expected a Type to find rootObj");

    object? currentObj = typeObj;

    var staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    var instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    var currentBindingFlags = staticFlags;

    do
    {
      var item = enumerator.Current;
      currentObj = item.Type switch
      {
        CustomSerializerObjectReferenceItemType.Field => type.GetField(item.Name, currentBindingFlags)
          ?.GetValue(currentObj),
        CustomSerializerObjectReferenceItemType.Property => type.GetProperty(item.Name, currentBindingFlags)
          ?.GetValue(currentObj),
        _ => null
      };
      if (currentObj == null)
        throw new Exception($"Expected to find member {item.Name} on type {type.FullName}");

      type = currentObj.GetType();
      currentBindingFlags = instanceFlags;
    } while (enumerator.MoveNext());

    return currentObj;
  }

  public void Serialize(Stream stream, JsonWriterOptions options)
  {
    var rootObj = GetRootObjectReference();
    if (rootObj == null)
      throw new Exception("Expected to find rootObj");
    var writer = new Utf8JsonWriter(stream, options);
    Schema.Write(writer, rootObj);
    writer.Flush();
  }
}

[JsonSerializable(typeof(CustomJsonSerializer))]
[JsonSerializable(typeof(DataType))]
[JsonSerializable(typeof(Dictionary<string, TypeSerializationDescription>))]
[JsonSerializable(typeof(Dictionary<string, CustomJsonSerializer>))]
[JsonSourceGenerationOptions(UseStringEnumConverter = true, WriteIndented = true)]
public partial class CustomJsonSerializerContext : JsonSerializerContext;

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
      return null;
    if (type == typeof(UnityEngine.Sprite))
      return null;
    if (type.FullName.StartsWith("UnityEngine") && !type.FullName.Contains("Vector"))
      return null;
    if (type == typeof(VertaBuffer) || type == typeof(WreckageHandler) || type == typeof(WreckageFragment))
      return null;
    if (IsTrivialType(type))
      return null;
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
