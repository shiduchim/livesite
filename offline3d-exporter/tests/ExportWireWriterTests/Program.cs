using System.Globalization;
using System.Text;
using System.Text.Json;
using Offline3D;
int passed=0;
void Check(string name,Action f){f();passed++;Console.WriteLine("PASS "+name);}
void Equal(bool result){if(!result)throw new Exception("Assertion failed");}
void Reject(Action f){try{f();}catch(InvalidDataException){return;}throw new Exception("Expected InvalidDataException");}
JsonElement Read(byte[] value)=>JsonDocument.Parse(value,new JsonDocumentOptions{MaxDepth=64}).RootElement.Clone();
void Equivalent(JsonElement a,JsonElement b){
 Equal(a.ValueKind==b.ValueKind);
 switch(a.ValueKind){
  case JsonValueKind.Object:
   Equal(a.EnumerateObject().Count()==b.EnumerateObject().Count());foreach(var p in a.EnumerateObject()){Equal(b.TryGetProperty(p.Name,out var other));Equivalent(p.Value,other);}break;
  case JsonValueKind.Array:Equal(a.GetArrayLength()==b.GetArrayLength());for(int i=0;i<a.GetArrayLength();i++)Equivalent(a[i],b[i]);break;
  case JsonValueKind.Number:Equal(a.GetDouble().Equals(b.GetDouble()));break;
  case JsonValueKind.String:Equal(a.GetString()==b.GetString());break;
 }
}
var policy=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true};
Check("camel-case DTO property names match original serializer",()=>{var data=new{UniqueId="abc",DocumentId="doc",LevelProjectElevationFeet=33.5,URLValue="x",URL="url",X=1,Matrix=new{Values=new[]{1d,2,3}}};Equivalent(Read(Contract.Encode(data)),Read(JsonSerializer.SerializeToUtf8Bytes(data,policy)));});
Check("exact glTF names preserve POSITION and NORMAL",()=>{var data=new{attributes=new{POSITION=0,NORMAL=1},byteLength=72,mode=4};var result=Read(Contract.EncodeExact(data));Equivalent(result,Read(JsonSerializer.SerializeToUtf8Bytes(data)));Equal(result.GetProperty("attributes").TryGetProperty("POSITION",out _));});
Check("null and empty arrays preserve JSON shape",()=>Equivalent(Read(Contract.Encode(new{Items=Array.Empty<string>(),Null=(object?)null})),Read(JsonSerializer.SerializeToUtf8Bytes(new{Items=Array.Empty<string>(),Null=(object?)null},policy))));
Check("control characters quotes backslashes Hebrew and emoji round-trip",()=>{var text=new string(Enumerable.Range(0,32).Select(x=>(char)x).ToArray())+"\"\\<>&' שלום 😀\u2028\u2029";Equal(Read(Contract.Encode(text)).GetString()==text);});
Check("malformed high surrogate is rejected",()=>Reject(()=>Contract.Encode("\ud800")));
Check("malformed low surrogate is rejected",()=>Reject(()=>Contract.Encode("\udc00")));
Check("high surrogate followed by ordinary character is rejected",()=>Reject(()=>Contract.Encode("\ud800x")));
Check("NaN is rejected",()=>Reject(()=>Contract.Encode(new{Value=double.NaN})));
Check("positive infinity is rejected",()=>Reject(()=>Contract.Encode(double.PositiveInfinity)));
Check("negative infinity float is rejected",()=>Reject(()=>Contract.Encode(float.NegativeInfinity)));
Check("floating point exponent and tiny values round-trip",()=>{var values=new[]{double.Epsilon,double.MaxValue,-double.Epsilon,-0d,1.2345678901234567};var result=Read(Contract.Encode(values));for(int i=0;i<values.Length;i++)Equal(result[i].GetDouble().Equals(values[i]));});
Check("numbers ignore process decimal culture",()=>{var saved=CultureInfo.CurrentCulture;try{CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");Equal(Read(Contract.Encode(new{Value=1.25})).GetProperty("value").GetDouble()==1.25);}finally{CultureInfo.CurrentCulture=saved;}});
Check("integer ranges remain exact",()=>{var result=Read(Contract.Encode(new{Min=long.MinValue,Max=ulong.MaxValue}));Equal(result.GetProperty("min").GetInt64()==long.MinValue&&result.GetProperty("max").GetUInt64()==ulong.MaxValue);});
Check("string dictionary keys are preserved exactly",()=>{var result=Read(Contract.Encode(new Dictionary<string,object?>{{"UPPER",1},{"Null",null}}));Equal(result.TryGetProperty("UPPER",out _)&&result.TryGetProperty("Null",out _));});
Check("non-string dictionary keys are rejected",()=>Reject(()=>Contract.Encode(new System.Collections.Hashtable{{1,"x"}})));
Check("cyclic sequence is rejected",()=>{var value=new List<object>();value.Add(value);Reject(()=>Contract.Encode(value));});
Check("shared sibling reference is accepted",()=>{var shared=new{A=1};var result=Read(Contract.Encode(new[]{shared,shared}));Equal(result.GetArrayLength()==2);});
Check("thirty-two nested containers are accepted",()=>{object value=0;for(int i=0;i<32;i++)value=new[]{value};Read(Contract.Encode(value));});
Check("thirty-three nested containers are rejected",()=>{object value=0;for(int i=0;i<33;i++)value=new[]{value};Reject(()=>Contract.Encode(value));});
Check("foreign framework objects are rejected",()=>Reject(()=>Contract.Encode(DateTime.UtcNow)));
Check("SHA256 matches established digest",()=>Equal(Contract.Hash(Encoding.UTF8.GetBytes("abc"))=="ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
Check("JSON output is bounded at 32 MiB",()=>Reject(()=>Contract.Encode(new string('a',Contract.MaxResource))));
Console.WriteLine(passed+" exporter JSON writer checks passed on Linux .NET 8; production writer has no JSON library dependency.");
