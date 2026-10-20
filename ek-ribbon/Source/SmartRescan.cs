using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EKRibbon
{
    // General, opt-in maintenance for a saved shared layout. Triggered only by
    // an explicit successful Rescan; startup never rewrites command bindings.
    internal static class SmartRescan
    {
        internal sealed class CommandSwap
        {
            internal CommandInfo Previous;
            internal CommandInfo ReplacementCommand;
            internal CommandInfo Replacement { get { return ReplacementCommand; } }
        }
        internal sealed class MigrationPlan
        {
            internal readonly List<CommandSwap> Changes=new List<CommandSwap>();
            internal readonly List<CommandInfo> MetadataUpdates=new List<CommandInfo>();
            internal readonly List<string> Missing=new List<string>();
            internal readonly List<string> Skipped=new List<string>();
        }
        static string Normalize(string s)
        {
            return new string((s??"").Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant).ToArray());
        }
        // Match renamed "Print Sheets" -> "Sheet Print" generically, without
        // hard-coded app names. Never use fuzzy edit distance to guess commands.
        static string ActionSignature(string s)
        {
            var words=System.Text.RegularExpressions.Regex.Matches((s??"").ToUpperInvariant(),@"[A-Z0-9]+");
            var list=new List<string>();
            foreach(System.Text.RegularExpressions.Match word in words)
            {
                string w=word.Value;
                if(w.Length>4 && w.EndsWith("S",StringComparison.Ordinal) &&
                    !w.EndsWith("SS",StringComparison.Ordinal))w=w.Substring(0,w.Length-1);
                list.Add(w);
            }
            list.Sort(StringComparer.Ordinal);
            return string.Join("|",list);
        }
        static bool ExecutableCSharp(CommandInfo ci)
        {
            if(ci==null || !ExternalCommands.IsExternal(ci.Id) || !ci.Registered ||
                string.IsNullOrWhiteSpace(ci.ClassName) ||
                string.IsNullOrWhiteSpace(ci.AssemblyPath))return false;
            try
            {
                return File.Exists(ci.AssemblyPath) &&
                    ci.AssemblyPath.IndexOf("pyRevit",StringComparison.OrdinalIgnoreCase)<0 &&
                    ci.ClassName.IndexOf("pyRevit",StringComparison.OrdinalIgnoreCase)<0;
            }
            catch{return false;}
        }
        static bool Same(string a,string b)
        {return Normalize(a)==Normalize(b);}
        static int MatchScore(CommandInfo old,CommandInfo newer)
        {
            if(old==null || newer==null || Same(old.Name,"") || Same(newer.Name,""))return 0;
            bool exact=Same(old.Name,newer.Name);
            bool reordered=ActionSignature(old.Name)==ActionSignature(newer.Name);
            if(!exact && !reordered)return 0;
            bool tab=Same(old.Tab,newer.Tab);
            bool panel=Same(old.Panel,newer.Panel);
            if(tab && panel)return exact?100:90;
            // Moving a command to a different panel can be legitimate, but
            // require a precise title, at least two words, and unique match.
            // Never guess generic one-word buttons such as "Settings".
            bool descriptive=ActionSignature(old.Name).Contains("|");
            if(descriptive && exact && tab)return 70;
            if(descriptive && exact && panel)return 65;
            return 0;
        }
        internal static MigrationPlan Plan(int year,Layout layout,
            IEnumerable<CommandInfo> allLive,IEnumerable<CommandInfo> registeredExternal)
        {
            var plan=new MigrationPlan();
            var live=(allLive??Enumerable.Empty<CommandInfo>()).Where(x=>x!=null)
                .GroupBy(x=>x.Key,StringComparer.OrdinalIgnoreCase)
                .Select(g=>g.OrderByDescending(c=>ExecutableCSharp(c)).First()).ToList();
            var liveKeys=new HashSet<string>(live.Select(x=>x.Key),StringComparer.OrdinalIgnoreCase);
            var candidates=(registeredExternal??Enumerable.Empty<CommandInfo>())
                .Where(ExecutableCSharp)
                .GroupBy(x=>x.Key,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
            // Include the embedded definitions to support removed Python apps.
            var known=layout.Definitions.Concat(State.ReadForBuild(year,layout))
                .Where(x=>x!=null && !string.IsNullOrWhiteSpace(x.Key))
                .GroupBy(x=>x.Key,StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);
            var proposals=new List<CommandSwap>();
            foreach(string key in layout.Order.Where(x=>layout.Selected.Contains(x))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if(!known.TryGetValue(key,out var prior))continue;
                bool present=liveKeys.Contains(key);
                if(present)
                {
                    var current=live.First(x=>string.Equals(x.Key,key,StringComparison.OrdinalIgnoreCase));
                    // A refresh of the SAME executable identity may update its icon
                    // and caption; never interpret it as a different command.
                    if(current.Registered && !string.IsNullOrWhiteSpace(current.Icon) &&
                        File.Exists(current.Icon) &&
                        (!string.Equals(prior.Icon,current.Icon,StringComparison.OrdinalIgnoreCase) ||
                         !string.Equals(prior.Name,current.Name,StringComparison.Ordinal)))
                        plan.MetadataUpdates.Add(current);
                    // When an old Python proxy and new C# command still COEXIST,
                    // offer an exact same-location and same-name update only for
                    // the custom EK / pyRevit tabs; do not silently reinterpret
                    // ordinary native commands or replace working C# identities.
                    if(ExternalCommands.IsExternal(prior.Id) ||
                        (!Same(prior.Tab,"EK") && !Same(prior.Tab,"pyRevit")))continue;
                }
                else plan.Missing.Add((prior.Tab??"")+" / "+(prior.Panel??"")+" / "+(prior.Name??key));
                // Search only proven executable, installed C# buttons. Do NOT
                // replace missing native Revit commands with vaguely similar ones.
                if((prior.Tab??"").Equals("Modify",StringComparison.OrdinalIgnoreCase) ||
                    (prior.Id??"").StartsWith("POSTABLE:",StringComparison.OrdinalIgnoreCase))continue;
                var matches=candidates.Where(x=>!layout.Selected.Contains(x.Key) &&
                        !string.Equals(x.Key,key,StringComparison.OrdinalIgnoreCase))
                    .Select(x=>new{Command=x,Score=MatchScore(prior,x)})
                    // An installed old proxy can be replaced only by an exact
                    // same-tab/panel/caption C# candidate, with user approval.
                    .Where(x=>x.Score>0 && (!present || x.Score==100))
                    .OrderByDescending(x=>x.Score).ToList();
                if(matches.Count==0)continue;
                int best=matches[0].Score;
                if(matches.Count(x=>x.Score==best)!=1)
                {
                    plan.Skipped.Add(prior.Name+": multiple equally likely C# commands");
                    continue;
                }
                proposals.Add(new CommandSwap{Previous=prior,ReplacementCommand=matches[0].Command});
            }
            foreach(var group in proposals.GroupBy(x=>x.Replacement.Key,StringComparer.OrdinalIgnoreCase))
            {
                if(group.Count()!=1)
                {
                    foreach(var conflict in group)
                        plan.Skipped.Add(conflict.Previous.Name+": several old buttons target one new command");
                    continue;
                }
                plan.Changes.Add(group.First());
            }
            return plan;
        }
        static void MoveMap(Dictionary<string,int> dict,string from,string to)
        {
            if(dict.TryGetValue(from,out var value))
            {dict.Remove(from);dict[to]=value;}
        }
        internal static Layout ApplyAndSave(int year,Layout original,MigrationPlan plan,
            IEnumerable<CommandInfo> liveCommands)
        {
            if(plan==null||(plan.Changes.Count==0&&plan.MetadataUpdates.Count==0))
                throw new InvalidOperationException("No changes to save.");
            Layout next=original.Clone();
            foreach(var change in plan.Changes)
            {
                string oldKey=change.Previous.Key,newKey=change.Replacement.Key;
                if(!next.Selected.Contains(oldKey) || next.Selected.Contains(newKey) ||
                    !next.Order.Contains(oldKey))
                    throw new InvalidOperationException("Ribbon selection changed. Rescan before trying again.");
                int position=next.Order.IndexOf(oldKey);
                next.Order[position]=newKey;
                next.Selected.Remove(oldKey);next.Selected.Add(newKey);
                if(next.Large.Remove(oldKey))next.Large.Add(newKey);
                MoveMap(next.Zoom,oldKey,newKey);
                next.Definitions.RemoveAll(x=>x.Key==oldKey || x.Key==newKey);
                next.Definitions.Add(change.Replacement);
            }
            foreach(var ci in plan.MetadataUpdates)
            {
                if(!next.Selected.Contains(ci.Key))continue;
                next.Definitions.RemoveAll(x=>string.Equals(x.Key,ci.Key,StringComparison.OrdinalIgnoreCase));
                next.Definitions.Add(ci);
            }
            // Timestamped backup before the shared layout changes.
            string directory=Path.Combine(State.Root,"Backups","SmartRescan_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(directory);
            if(File.Exists(State.LayoutFile))File.Copy(State.LayoutFile,Path.Combine(directory,Path.GetFileName(State.LayoutFile)),false);
            State.UpdateDefinitions(year,next,liveCommands);
            State.Save(next);
            return next;
        }
    }
}
