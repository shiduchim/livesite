using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Offline3D;

/// <summary>Writes only trusted exporter DTOs. Revit never reads JSON here;
/// parsing and full contract/GLB validation stay in the self-contained helper.
/// No NuGet JSON package or shared Core assembly is loaded into the Revit host.</summary>
public static class Contract
{
    public const int MaxResource=64*1024*1024;
    private const int MaxDepth=32;
    private static readonly Dictionary<Type,PropertyInfo[]> Properties=new Dictionary<Type,PropertyInfo[]>();
    private static readonly object PropertyGate=new object();

    public static byte[] Encode<T>(T value)=>EncodeValue(value,true);
    public static byte[] EncodeExact<T>(T value)=>EncodeValue(value,false);
    public static string Hash(byte[] bytes)
    {
        if(bytes==null)throw new ArgumentNullException(nameof(bytes));
        using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
    }

    private static byte[] EncodeValue(object? value,bool camelCase)
    {
        using(var output=new BoundedOutput()){
            using(var writer=new StreamWriter(output,new UTF8Encoding(false,true),4096,true)){
                WriteValue(writer,value,camelCase,0,new HashSet<object>(ReferenceComparer.Instance));
                writer.Flush();
            }
            return output.ToArray();
        }
    }

    private static void WriteValue(TextWriter writer,object? value,bool camelCase,int depth,HashSet<object> parents)
    {
        if(value==null){writer.Write("null");return;}
        if(value is string text){WriteString(writer,text);return;}
        if(value is char character){WriteString(writer,character.ToString());return;}
        if(value is bool boolean){writer.Write(boolean?"true":"false");return;}
        if(value is double number){if(double.IsNaN(number)||double.IsInfinity(number))throw new InvalidDataException("Non-finite JSON number");writer.Write(number.ToString("R",CultureInfo.InvariantCulture));return;}
        if(value is float single){if(float.IsNaN(single)||float.IsInfinity(single))throw new InvalidDataException("Non-finite JSON number");writer.Write(single.ToString("R",CultureInfo.InvariantCulture));return;}
        if(value is decimal dec){writer.Write(dec.ToString(CultureInfo.InvariantCulture));return;}
        if(value is byte||value is sbyte||value is short||value is ushort||value is int||value is uint||value is long||value is ulong){writer.Write(((IFormattable)value).ToString(null,CultureInfo.InvariantCulture));return;}
        var type=value.GetType();
        if(type.IsEnum){WriteValue(writer,Convert.ChangeType(value,Enum.GetUnderlyingType(type),CultureInfo.InvariantCulture),camelCase,depth,parents);return;}
        if(depth>=MaxDepth)throw new InvalidDataException("JSON exceeds maximum depth");
        bool tracked=!type.IsValueType;
        if(tracked&&!parents.Add(value))throw new InvalidDataException("Cyclic exporter data");
        try{
            if(value is IDictionary dictionary){
                writer.Write('{');bool first=true;
                foreach(DictionaryEntry entry in dictionary){
                    if(!(entry.Key is string key))throw new InvalidDataException("Only string JSON dictionary keys are supported");
                    if(!first)writer.Write(',');first=false;WriteString(writer,key);writer.Write(':');WriteValue(writer,entry.Value,camelCase,depth+1,parents);
                }
                writer.Write('}');return;
            }
            if(value is IEnumerable sequence){
                writer.Write('[');bool first=true;
                foreach(var item in sequence){if(!first)writer.Write(',');first=false;WriteValue(writer,item,camelCase,depth+1,parents);}
                writer.Write(']');return;
            }
            bool anonymous=type.IsDefined(typeof(CompilerGeneratedAttribute),false)&&type.Name.IndexOf("AnonymousType",StringComparison.Ordinal)>=0;
            if(type.Namespace!="Offline3D"&&!anonymous)throw new InvalidDataException("Unsupported exporter JSON type: "+type.FullName);
            PropertyInfo[] properties;
            lock(PropertyGate){if(!Properties.TryGetValue(type,out properties!)){
                properties=type.GetProperties(BindingFlags.Instance|BindingFlags.Public).Where(p=>p.CanRead&&p.GetGetMethod(false)!=null&&p.GetIndexParameters().Length==0).OrderBy(p=>p.MetadataToken).ToArray();
                Properties.Add(type,properties);
            }}
            writer.Write('{');bool firstProperty=true;
            foreach(var property in properties){
                if(!firstProperty)writer.Write(',');firstProperty=false;
                WriteString(writer,camelCase?CamelCase(property.Name):property.Name);writer.Write(':');
                WriteValue(writer,property.GetValue(value,null),camelCase,depth+1,parents);
            }
            writer.Write('}');
        }finally{if(tracked)parents.Remove(value);}
    }

    private static string CamelCase(string name)
    {
        if(name.Length==0||!char.IsUpper(name[0]))return name;
        var chars=name.ToCharArray();
        for(int i=0;i<chars.Length;i++){
            if(i==1&&!char.IsUpper(chars[i]))break;
            if(i>0&&i+1<chars.Length&&!char.IsUpper(chars[i+1])){
                if(chars[i+1]==' ')chars[i]=char.ToLowerInvariant(chars[i]);
                break;
            }
            chars[i]=char.ToLowerInvariant(chars[i]);
        }
        return new string(chars);
    }

    private static void WriteString(TextWriter writer,string value)
    {
        writer.Write('"');
        for(int i=0;i<value.Length;i++){
            char c=value[i];
            if(char.IsHighSurrogate(c)){
                if(i+1>=value.Length||!char.IsLowSurrogate(value[i+1]))throw new InvalidDataException("Unpaired Unicode surrogate in exporter text");
                Escape(writer,c);Escape(writer,value[++i]);continue;
            }
            if(char.IsLowSurrogate(c))throw new InvalidDataException("Unpaired Unicode surrogate in exporter text");
            switch(c){
                case '"':writer.Write("\\\"");break;
                case '\\':writer.Write("\\\\");break;
                case '\b':writer.Write("\\b");break;
                case '\f':writer.Write("\\f");break;
                case '\n':writer.Write("\\n");break;
                case '\r':writer.Write("\\r");break;
                case '\t':writer.Write("\\t");break;
                default:if(c<' '||c>'~'||c=='<'||c=='>'||c=='&'||c=='\'')Escape(writer,c);else writer.Write(c);break;
            }
        }
        writer.Write('"');
    }
    private static void Escape(TextWriter writer,char value){writer.Write("\\u");writer.Write(((int)value).ToString("x4",CultureInfo.InvariantCulture));}

    private sealed class ReferenceComparer:IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance=new ReferenceComparer();
        public new bool Equals(object? x,object? y)=>ReferenceEquals(x,y);
        public int GetHashCode(object value)=>RuntimeHelpers.GetHashCode(value);
    }
    private sealed class BoundedOutput:Stream
    {
        private readonly MemoryStream inner=new MemoryStream();
        public override bool CanRead=>false;
        public override bool CanSeek=>false;
        public override bool CanWrite=>true;
        public override long Length=>inner.Length;
        public override long Position{get=>inner.Position;set=>throw new NotSupportedException();}
        public override void Flush()=>inner.Flush();
        public override void Write(byte[] buffer,int offset,int count){if(count<0||inner.Length+count>MaxResource)throw new InvalidDataException("Exporter JSON exceeds 64 MiB");inner.Write(buffer,offset,count);}
        public override void WriteByte(byte value){if(inner.Length>=MaxResource)throw new InvalidDataException("Exporter JSON exceeds 64 MiB");inner.WriteByte(value);}
        public byte[] ToArray()=>inner.ToArray();
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing){if(disposing)inner.Dispose();base.Dispose(disposing);}
    }
}
