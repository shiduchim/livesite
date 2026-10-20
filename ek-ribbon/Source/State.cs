using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EKRibbon
{
    // Files under %APPDATA%\EKRibbonCSharp. One layout is shared by Revit 2024-2027;
    // each year keeps its own catalog of the commands it found on its ribbon.
    internal static class State
    {
        internal static readonly string Root=Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"EKRibbonCSharp");
        internal static string LayoutFile {get{return Path.Combine(Root,"layout_shared.json");}}
        // Written by v1.x only. Still read so older installs keep every saved button.
        internal static string LegacyCommands {get{return Path.Combine(Root,"selected_commands.json");}}
        internal static string Icons {get{return Path.Combine(Root,"Icons");}}
        internal static string Catalog(int year) {return Path.Combine(Root,"catalog_"+year+".json");}

        static readonly object gate=new object();

        internal static Layout Load()
        {
            lock(gate)
            {
                return File.Exists(LayoutFile)?Layout.FromJson(Json.Load(LayoutFile)):new Layout();
            }
        }

        internal static void Save(Layout layout)
        {
            lock(gate)
            {
                Directory.CreateDirectory(Root);
                Json.Save(LayoutFile,layout.ToJson());
            }
        }

        // Changes one setting on disk without touching unsaved edits elsewhere.
        internal static void Update(Action<Layout> change)
        {
            lock(gate)
            {
                var layout=Load();
                change(layout);
                Save(layout);
            }
        }

        static IEnumerable<CommandInfo> Rows(string path,string property)
        {
            foreach(var row in Json.Arr(Json.Get(Json.Load(path),property)))
            {
                var ci=CommandInfo.FromJson(row);
                if(Json.Bool(Json.Get(Json.Obj(row),"smart")))continue;
                if(ci.Key.Length==0&&string.IsNullOrWhiteSpace(ci.Id))continue;
                ci.Key=CommandInfo.MakeKey(ci);
                yield return ci;
            }
        }

        // Only ever this year's inventory: another year's catalog says nothing about what is installed here.
        internal static List<CommandInfo> ReadCatalog(int year)
        {
            return Rows(Catalog(year),"items").GroupBy(x=>x.Key,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
        }

        internal static void WriteCatalog(int year,List<CommandInfo> items)
        {
            Directory.CreateDirectory(Root);
            Json.Save(Catalog(year),new Dictionary<string,object>{{"items",items.Select(x=>x.ToJson()).ToList()}});
        }

        // Definitions for every selected button, best source first.
        internal static List<CommandInfo> ReadForBuild(int year,Layout layout)
        {
            var available=new Dictionary<string,CommandInfo>(StringComparer.OrdinalIgnoreCase);
            Action<IEnumerable<CommandInfo>,bool> add=(rows,selectedOnly)=>{
                foreach(var ci in rows)
                    if((!selectedOnly||layout.Selected.Contains(ci.Key))&&!available.ContainsKey(ci.Key))available[ci.Key]=ci;
            };
            var local=ReadCatalog(year);
            add(local,false);
            add(layout.Definitions,true);
            add(Rows(LegacyCommands,"items"),true);
            foreach(int other in Enumerable.Range(2024,4).Where(y=>y!=year))add(ReadCatalog(other),true);
            // Add-ins sometimes give a button a different command ID per Revit year.
            // Prefer this year's button at the same tab / panel / caption, keeping the shared key.
            foreach(string key in layout.Selected)
            {
                if(!available.TryGetValue(key,out var wanted)||local.Any(x=>x.Key==key)||string.IsNullOrWhiteSpace(wanted.Name))continue;
                var match=local.FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x.Id)&&
                    string.Equals((x.Name??"").Trim(),wanted.Name.Trim(),StringComparison.OrdinalIgnoreCase)&&
                    string.Equals((x.Tab??"").Trim(),(wanted.Tab??"").Trim(),StringComparison.OrdinalIgnoreCase)&&
                    string.Equals((x.Panel??"").Trim(),(wanted.Panel??"").Trim(),StringComparison.OrdinalIgnoreCase));
                if(match==null)continue;
                var copy=CommandInfo.FromJson(match.ToJson());
                copy.Key=key;
                available[key]=copy;
            }
            var modify=KnownAddins.ModifyCommand(year);
            if(modify!=null&&layout.Selected.Contains(modify.Key))available[modify.Key]=modify;
            return available.Values.ToList();
        }

        // Embeds a definition for every selected button in the layout, so a Revit year
        // that never scanned the command can still build it.
        internal static void UpdateDefinitions(int year,Layout layout,IEnumerable<CommandInfo> current)
        {
            var known=new Dictionary<string,CommandInfo>(StringComparer.OrdinalIgnoreCase);
            foreach(var ci in ReadForBuild(year,layout))known[ci.Key]=ci;
            foreach(var ci in layout.Definitions)known[ci.Key]=ci;
            // The live scan has the freshest name and icon.
            foreach(var ci in current??Enumerable.Empty<CommandInfo>())
                if(layout.Selected.Contains(ci.Key)&&!string.IsNullOrWhiteSpace(ci.Id))known[ci.Key]=ci;
            layout.Definitions=layout.Selected.Where(known.ContainsKey).Select(k=>known[k])
                .Where(ci=>!string.IsNullOrWhiteSpace(ci.Id)).OrderBy(ci=>ci.Key).ToList();
        }
    }
}
