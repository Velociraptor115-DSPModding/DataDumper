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
[JsonDerivedType(typeof(TextureType), typeDiscriminator: "texture")]
[JsonDerivedType(typeof(SpriteType), typeDiscriminator: "sprite")]
public abstract class DataType
{
  public void RecursiveWrite(ISerializationContextProvider ctx, string pathItem, Utf8JsonWriter writer, object? value)
  {
    ctx.PushContext(pathItem);
    if (value == null)
    {
      writer.WriteNullValue();
      return;
    }
    Write(ctx, writer, value);
    ctx.PopContext();
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

public abstract class ImageType : DataType
{
  protected abstract byte[]? GetPngBytes(object value);
  
  public override void Write(ISerializationContextProvider ctx, Utf8JsonWriter writer, object value)
  {
    if (!ctx.ImageRipEnabled)
    {
      writer.WriteStringValue("__ImageRip__");
      return;
    }

    var bytes = GetPngBytes(value);
    if (bytes is not { Length: > 0 })
    {
      writer.WriteNullValue();
      return;
    }

    var relativePath = $"{string.Join("/", ctx.ContextPath)}.png";
    var ripPath = Path.Combine(ctx.RipRootPath, relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(ripPath));
    File.WriteAllBytes(ripPath, bytes);
    writer.WriteStringValue(relativePath);
  }
}

public class SpriteType : ImageType
{
  protected override byte[]? GetPngBytes(object value)
  {
    if (value is Sprite sprite)
      return TextureRip.GetPngBytes(sprite);
    return null;
  }
}

public class TextureType : ImageType
{
  protected override byte[]? GetPngBytes(object value)
  {
    if (value is Texture texture)
      return TextureRip.GetPngBytes(texture);
    return null;
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
    for (int i = 0; i < array.Count; i++)
    {
      ElementType.RecursiveWrite(ctx, i.ToString(), writer, array[i]);
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
        member.DataType.RecursiveWrite(ctx, member.Name, writer, memberValue);
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
        ctx.GetDataType(fieldType).RecursiveWrite(ctx, field.Name, writer, fieldVal);
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
        ctx.GetDataType(propertyType).RecursiveWrite(ctx, property.Name, writer, propertyVal);
      }
    }
    writer.WriteEndObject();
  }
}
