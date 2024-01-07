using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DysonSphereProgram.Modding.DataDumper;

public class SerializationContextProvider(CustomJsonSerializer serializer, string ripRootPath) : ISerializationContextProvider
{
  public bool WriteNull => serializer.WriteNull;
  public bool WriteEmpty => serializer.WriteEmpty;
  public bool WriteDefault => serializer.WriteDefault;
  public bool ShouldSerializeType(Type type) => serializer.ShouldSerializeType(type);
  public DataType GetDataType(Type type) => serializer.GetDataType(type);

  private readonly List<string> _contextPath = new();
  public IReadOnlyList<string> ContextPath => _contextPath;
  public void PushContext(string pathItem) => _contextPath.Add(pathItem);
  public void PopContext() => _contextPath.RemoveAt(_contextPath.Count - 1);

  public bool ImageRipEnabled => serializer.ImageRipEnabled;
  public string RipRootPath => ripRootPath;
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
  public Dictionary<string, DataType> TypeSchemas { get; set; } = new();
  public List<string> ExcludeTypes { get; set; } = new();
  public List<string> IncludeTypes { get; set; } = new();
  public List<string> ExcludeNamespaces { get; set; } = new();
  public List<string> IncludeNamespaces { get; set; } = new();
  public bool UseImplicitInferenceForUnknownTypes { get; set; }
  public bool WriteNull { get; set; }
  public bool WriteEmpty { get; set; }
  public bool WriteDefault { get; set; }
  public bool ImageRipEnabled { get; set; }
  
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
    using var enumerator = RootObject.GetEnumerator();
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

  public void Serialize(string ripRootPath, Stream stream, JsonWriterOptions options)
  {
    var rootObj = GetRootObjectReference();
    if (rootObj == null)
      throw new Exception("Expected to find rootObj");
    using var writer = new Utf8JsonWriter(stream, options);
    Schema.Write(new SerializationContextProvider(this, ripRootPath), writer, rootObj);
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
