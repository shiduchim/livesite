using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace EKRibbon
{
    // Dependency-free JSON reader/writer so the same sources build on net48, net8 and net10.
    internal static class Json
    {
        public static object Parse(string json) { return new Parser(json ?? "").Value(); }
        public static string Dump(object obj) { var b = new StringBuilder(); Write(b, obj); return b.ToString(); }
        public static Dictionary<string, object> Obj(object v) { return v as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        public static List<object> Arr(object v) { return v as List<object> ?? new List<object>(); }
        public static object Get(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) ? v : null; }
        public static string Str(object v, string def = "") { return v == null ? def : Convert.ToString(v, CultureInfo.InvariantCulture) ?? def; }
        public static bool Bool(object v) { return v is bool && (bool)v; }
        public static int Int(object v, int fallback = 0) { int i; return int.TryParse(Str(v), out i) ? i : fallback; }
        public static object ParseFileArray(string p) { try { return Parse(File.ReadAllText(p, Encoding.UTF8)); } catch { return new List<object>(); } }
        public static Dictionary<string, object> Load(string p)
        {
            // Backups are recovery copies only; never prefer a backup when the
            // current JSON is valid (including an intentionally empty layout).
            try { return Obj(Parse(File.ReadAllText(p, Encoding.UTF8))); }
            catch
            {
                try {return Obj(Parse(File.ReadAllText(p + ".backup", Encoding.UTF8)));}
                catch { return new Dictionary<string, object>(); }
            }
        }
        public static void Save(string p, object data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            string tmp = p + ".new";
            File.WriteAllText(tmp, Dump(data), new UTF8Encoding(false));
            if (File.Exists(p))
            {
                // The old Delete+Move implementation had an interval when
                // another Revit process could read a missing shared layout.
                // File.Replace is atomic on a local NTFS volume and preserves
                // the previous complete JSON file as a backup.
                File.Replace(tmp, p, p + ".backup", true);
            }
            else File.Move(tmp, p);
        }
        private static void Write(StringBuilder b, object o)
        {
            if (o == null) { b.Append("null"); return; }
            if (o is string) { b.Append('"'); foreach (char c in (string)o)
                { switch(c) { case '\\': b.Append("\\\\"); break; case '"': b.Append("\\\""); break; case '\n': b.Append("\\n"); break; case '\r': b.Append("\\r"); break; case '\t': b.Append("\\t"); break; default: if (c < 32) b.Append("\\u" + ((int)c).ToString("x4")); else b.Append(c); break; } } b.Append('"'); return; }
            if (o is bool) { b.Append((bool)o ? "true" : "false"); return; }
            var dict = o as IDictionary;
            if (dict != null) { b.Append('{'); bool first = true; foreach (DictionaryEntry kv in dict) { if (!first) b.Append(','); first=false; Write(b, Convert.ToString(kv.Key)); b.Append(':'); Write(b, kv.Value); } b.Append('}'); return; }
            var list = o as IEnumerable;
            if (list != null) { b.Append('['); bool first=true; foreach (object v in list) { if(!first)b.Append(','); first=false; Write(b,v); } b.Append(']'); return; }
            b.Append(Convert.ToString(o, CultureInfo.InvariantCulture));
        }
        private sealed class Parser
        {
            readonly string s; int i;
            internal Parser(string text) { s=text; }
            void Space() { while(i<s.Length && char.IsWhiteSpace(s[i])) i++; }
            char Peek() { Space(); return i < s.Length ? s[i] : '\0'; }
            char Next() { if(i>=s.Length) throw new FormatException("Unexpected end of JSON"); return s[i++]; }
            void Must(char c) { if(Peek()!=c) throw new FormatException("Expected " + c); i++; }
            internal object Value()
            {
                char c=Peek(); if(c=='"') return String(); if(c=='{') return Object(); if(c=='[') return Array();
                if(c=='t' && s.Substring(i).StartsWith("true")) {i+=4;return true;}
                if(c=='f' && s.Substring(i).StartsWith("false")) {i+=5;return false;}
                if(c=='n' && s.Substring(i).StartsWith("null")) {i+=4;return null;}
                int st=i; while(i<s.Length && " ,]}\r\n\t".IndexOf(s[i])<0) i++;
                if(st==i) throw new FormatException("Unexpected token");
                string n=s.Substring(st,i-st); double d; if(double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture,out d)) return d;
                throw new FormatException("Invalid number");
            }
            string String()
            {
                Must('"'); var b=new StringBuilder(); while(true) { char c=Next(); if(c=='"')break; if(c!='\\') { b.Append(c); continue; }
                c=Next(); switch(c) {case 'n': b.Append('\n');break;case 'r':b.Append('\r');break;case 't':b.Append('\t');break;case 'b':b.Append('\b');break;case 'f':b.Append('\f');break;case '/':b.Append('/');break;case '"':b.Append('"');break;case '\\':b.Append('\\');break;case 'u':b.Append((char)Convert.ToInt32(s.Substring(i,4),16));i+=4;break;default:throw new FormatException("Invalid escape");} }
                return b.ToString();
            }
            object Object()
            {
                Must('{'); var d=new Dictionary<string,object>(); if(Peek()=='}'){i++;return d;}
                while(true) {string k=String(); Must(':'); d[k]=Value(); char c=Peek();if(c=='}'){i++;return d;}Must(',');}
            }
            object Array()
            {
                Must('[');var a=new List<object>();if(Peek()==']'){i++;return a;}
                while(true){a.Add(Value());char c=Peek();if(c==']'){i++;return a;}Must(',');}
            }
        }
    }
}
