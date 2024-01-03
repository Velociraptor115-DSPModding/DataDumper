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

public interface ISerializationContextProvider
{
  bool WriteNull { get; }
  bool WriteEmpty { get; }
  bool WriteDefault { get; }
  bool ShouldSerializeType(Type type);
  DataType GetDataType(Type type);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StringType), typeDiscriminator: "string")]
[JsonDerivedType(typeof(NumberType), typeDiscriminator: "number")]
[JsonDerivedType(typeof(BooleanType), typeDiscriminator: "bool")]
[JsonDerivedType(typeof(ArrayType), typeDiscriminator: "array")]
[JsonDerivedType(typeof(ObjectType), typeDiscriminator: "object")]
public abstract class DataType
{
  public void WriteWithNullHandling(ISerializationContextProvider ctx, Utf8JsonWriter writer, object? value)
  {
    if (value == null)
    {
      writer.WriteNullValue();
      return;
    }
    Write(ctx, writer, value);
  }
  public abstract void Write(ISerializationContextProvider ctx, Utf8JsonWriter writer, object value);
}

public class StringType : DataType
{
  public override void Write(ISerializationContextProvider ctx, Utf8JsonWriter writer, object value)
  {
    writer.WriteStringValue(Convert.ToString(value));
  }
}

public class NumberType : DataType
{
  public override void Write(ISerializationContextProvider ctx, Utf8JsonWriter writer, object value)
  {
    writer.WriteNumberValue(Convert.ToDecimal(value));
  }
}

public class BooleanType : DataType
{
  public override void Write(ISerializationContextProvider ctx, Utf8JsonWriter writer, object value)
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
  public override void Write(ISerializationContextProvider ctx, Utf8JsonWriter writer, object value)
  {
    var array = value as IList;
    if (array == null)
      throw new Exception("Expected IList");
    
    writer.WriteStartArray();
    foreach (var item in array)
    {
      ElementType.WriteWithNullHandling(ctx, writer, item);
    }
    writer.WriteEndArray();
  }
}

public class UnknownType : DataType
{
  private Type type;
  
  public UnknownType(Type type)
  {
    this.type = type;
  }
  
  public override void Write(ISerializationContextProvider ctx, Utf8JsonWriter writer, object value)
  {
    writer.WriteStartObject();
    writer.WriteBoolean("isUnknown", true);
    writer.WriteString("dotnetType", type.FullName);
    writer.WriteEndObject();
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
  
  private static readonly BindingFlags bFlagsInstance = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
  private static readonly BindingFlags bFlagsStatic = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

  public object? GetMember(object obj)
  {
    var isType = obj is Type;
    var type = isType ? obj as Type : obj.GetType();
    var bFlags = isType ? bFlagsStatic :  bFlagsInstance;
    var inst = isType ? null : obj;
    return Type switch
    {
      ObjectMemberType.Field => type.GetField(Name, bFlags)?.GetValue(inst),
      ObjectMemberType.Property => type.GetProperty(Name, bFlags)?.GetValue(inst),
      _ => null
    };
  }
}

public class ObjectType : DataType
{
  public ObjectType(List<ObjectMemberReference> members, bool useInference, bool @static, List<string> include, List<string> exclude)
  {
    Members = members ?? new();
    UseInference = useInference;
    Static = @static;
    Include = include ?? new();
    Exclude = exclude ?? new();
  }

  public List<ObjectMemberReference> Members { get; set; }
  public bool UseInference { get; set; }
  public bool Static { get; set; }
  public List<string> Include { get; set; }
  public List<string> Exclude { get; set; }
  public bool? WriteNull { get; set; }
  public bool? WriteEmpty { get; set; }
  public bool? WriteDefault { get; set; }
  
  private bool ShouldHandleMember(string name, Type type, ISerializationContextProvider ctx, HashSet<string> handledMembers)
  {
    if (handledMembers.Contains(name))
      return false;
    if (Exclude.Contains(name))
      return false;
    if (!Include.Contains(name))
    {
      if (Exclude.Contains("*"))
        return false;
      var normalizedType = TypeSerializationDescription.NormalizeInterestingType(type);
      if (normalizedType == null)
        return false;
      if (!ctx.ShouldSerializeType(normalizedType))
        return false;
    }

    return true;
  }
  
  private bool ShouldWriteMember(Type type, object? value, ISerializationContextProvider ctx)
  {
    if (value == null)
      return WriteNull ?? ctx.WriteNull;
    if (value is IList { Count: 0 })
      return WriteEmpty ?? ctx.WriteEmpty;
    if (value is "")
      return WriteEmpty ?? ctx.WriteEmpty;
    if (type.IsValueType)
    {
      var defaultVal = Activator.CreateInstance(type);
      if (value.Equals(defaultVal))
        return WriteDefault ?? ctx.WriteDefault;
    }

    return true;
  }

  public override void Write(ISerializationContextProvider ctx, Utf8JsonWriter writer, object value)
  {
    var handledMembers = new HashSet<string>();
    writer.WriteStartObject();
    foreach (var member in Members)
    {
      var memberValue = member.GetMember(value);

      if (memberValue != null)
      {
        writer.WritePropertyName(member.Name);
        member.DataType.WriteWithNullHandling(ctx, writer, memberValue);
        handledMembers.Add(member.Name);
      }
    }
    if (UseInference)
    {
      var isType = value is Type;
      var bFlags = BindingFlags.Public | BindingFlags.NonPublic | (isType ? BindingFlags.Static : BindingFlags.Instance);
      var type = isType ? value as Type : value.GetType();
      var inst = isType ? null : value;
      var fields = type.GetFields(bFlags);
      foreach (var field in fields)
      {
        var fieldType = field.FieldType;
        if (!ShouldHandleMember(field.Name, fieldType, ctx, handledMembers))
          continue;
        var fieldVal = field.GetValue(inst);
        if (!ShouldWriteMember(fieldType, fieldVal, ctx))
          continue;
        writer.WritePropertyName(field.Name);
        ctx.GetDataType(fieldType).WriteWithNullHandling(ctx, writer, fieldVal);
      }
      
      var properties = type.GetProperties(bFlags);
      foreach (var property in properties)
      {
        var propertyType = property.PropertyType;
        if (!ShouldHandleMember(property.Name, property.PropertyType, ctx, handledMembers))
          continue;
        var propertyVal = property.GetValue(inst);
        if (!ShouldWriteMember(propertyType, propertyVal, ctx))
          continue;
        writer.WritePropertyName(property.Name);
        ctx.GetDataType(propertyType).WriteWithNullHandling(ctx, writer, propertyVal);
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

public class CustomJsonSerializer: ISerializationContextProvider
{
  public List<CustomSerializerObjectReferenceItem> RootObject { get; set; }
  public DataType Schema { get; set; }
  public Dictionary<string, DataType> TypeSchemas { get; set; } = new();
  public List<string> ExcludeTypes { get; set; } = new();
  public List<string> IncludeTypes { get; set; } = new();
  public List<string> ExcludeNamespaces { get; set; } = new();
  public List<string> IncludeNamespaces { get; set; } = new();
  public bool UseImplicitInferenceForUnknownTypes { get; set; }
  public bool WriteNull { get; set; }
  public bool WriteEmpty { get; set; }
  public bool WriteDefault { get; set; }
  
  public bool ShouldSerializeType(Type type)
  {
    if (ExcludeTypes.Contains(type.FullName))
      return false;
    if (IncludeTypes.Contains(type.FullName))
      return true;
    if (IncludeNamespaces.Any(x => (type.Namespace ?? "ROOT").StartsWith(x)))
      return true;
    if (ExcludeNamespaces.Any(x => (type.Namespace ?? "ROOT").StartsWith(x)))
      return false;
    return true;
  }
  
  public DataType GetDataType(Type type)
  {
    if (TypeSchemas.ContainsKey(type.FullName))
      return TypeSchemas[type.FullName];
    if (type == typeof(bool))
      return new BooleanType();
    if (type.IsPrimitive || type == typeof(decimal))
      return new NumberType();
    if (type.IsEnum || type == typeof(string))
      return new StringType();
    if (type.IsArray)
      return new ArrayType(GetDataType(type.GetElementType()));
    var iListInterfaces = type.FindInterfaces((x, y) => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IList<>), null);
    if (iListInterfaces.Length > 0)
    {
      return new ArrayType(GetDataType(iListInterfaces[0].GenericTypeArguments[0]));
    }

    if (UseImplicitInferenceForUnknownTypes)
      return new ObjectType(new List<ObjectMemberReference>(), true, false, new List<string>(), new List<string>());
    else
      return new UnknownType(type);
  }

  private object? GetRootObjectReference()
  {
    var assembly = typeof(LDB).Assembly;
    object typeObj = assembly;
    var enumerator = RootObject.GetEnumerator();
    bool hasMore = false;
    while ((hasMore = enumerator.MoveNext()) && enumerator.Current.Type == CustomSerializerObjectReferenceItemType.Type)
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

    if (!hasMore)
      return currentObj;
    
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
    Schema.Write(this, writer, rootObj);
    writer.Flush();
  }
}

[JsonSerializable(typeof(CustomJsonSerializer))]
[JsonSerializable(typeof(DataType))]
[JsonSerializable(typeof(Dictionary<string, TypeSerializationDescription>))]
[JsonSerializable(typeof(Dictionary<string, CustomJsonSerializer>))]
[JsonSourceGenerationOptions(
  UseStringEnumConverter = true,
  WriteIndented = true,
  ReadCommentHandling = JsonCommentHandling.Skip,
  AllowTrailingCommas = true
)]
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
