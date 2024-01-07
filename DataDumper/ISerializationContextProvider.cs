using System;
using System.Collections.Generic;

namespace DysonSphereProgram.Modding.DataDumper;

public interface ISerializationContextProvider
{
  bool WriteNull { get; }
  bool WriteEmpty { get; }
  bool WriteDefault { get; }
  bool ShouldSerializeType(Type type);
  DataType GetDataType(Type type);
}
