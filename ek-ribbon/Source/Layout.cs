using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace EKRibbon
{
    // Pure data model shared by the ribbon builder and the settings window.
    // No Revit or WPF dependencies, so the editing rules are unit tested.
    internal sealed class CommandInfo
    {
        public string Key, Name, Id, Tab, Panel, Icon;
        // Supported Revit API metadata for external IExternalCommand ribbon buttons.
        public string AssemblyPath, ClassName;
        public bool Registered;

        // Identity survives between Revit 2024-2027: Revit can move or relabel
        // a button without changing its command ID.
        internal static string MakeKey(CommandInfo c)
        {
            if(c==null)return "";
            string text=!string.IsNullOrWhiteSpace(c.Id)
                ? "EKRB|ID|"+c.Id.Trim().ToUpperInvariant()
                : "EKRB|LOCATION|"+(c.Tab??"").ToUpperInvariant()+"|"+(c.Panel??"").ToUpperInvariant()+"|"+(c.Name??"").ToUpperInvariant();
            using(var sha=SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").Substring(0,12).ToLowerInvariant();
        }
        internal Dictionary<string,object> ToJson()
        {
            return new Dictionary<string,object>{{"key",Key},{"id",Id},{"name",Name},{"tab",Tab},{"panel",Panel},
                {"icon",Icon},{"assembly",AssemblyPath},{"class",ClassName},{"registered",Registered}};
        }
        internal static CommandInfo FromJson(object row)
        {
            var d=Json.Obj(row);
            return new CommandInfo{Key=Json.Str(Json.Get(d,"key")),Name=Json.Str(Json.Get(d,"name")),
                Id=Json.Str(Json.Get(d,"id")),Tab=Json.Str(Json.Get(d,"tab")),Panel=Json.Str(Json.Get(d,"panel")),
                Icon=Json.Str(Json.Get(d,"icon")),AssemblyPath=Json.Str(Json.Get(d,"assembly")),
                ClassName=Json.Str(Json.Get(d,"class")),Registered=Json.Bool(Json.Get(d,"registered"))};
        }
    }

    // One column of the panel: either a single large button or a stack of
    // 2/3 small cells. A cell holds a command key, a gap token or null (empty).
    internal sealed class RibbonColumn
    {
        internal bool Large;
        internal string[] Cells;
        internal IEnumerable<string> Tokens {get{return Cells.Where(c=>c!=null);}}
    }

    // Buttons between two dividers. Id is StartGroup or the divider token.
    internal sealed class RibbonGroup
    {
        internal string Id;
        internal int Rows;
        internal readonly List<RibbonColumn> Columns=new List<RibbonColumn>();
    }

    internal sealed class CellRef
    {
        internal int Group, Column, Row;
    }

    internal sealed class Layout
    {
        internal const string StartGroup="__start__";
        const string GapPrefix="__gap_", DividerPrefix="__divider_";

        // Command definitions travel with the shared layout so every Revit year can build it.
        public List<CommandInfo> Definitions=new List<CommandInfo>();
        // Tokens in ribbon order: command keys, divider tokens and gap tokens.
        public List<string> Order=new List<string>();
        public HashSet<string> Selected=new HashSet<string>();
        public HashSet<string> Dividers=new HashSet<string>();
        public Dictionary<string,int> GroupRows=new Dictionary<string,int>();
        public Dictionary<string,int> Zoom=new Dictionary<string,int>();
        public HashSet<string> Large=new HashSet<string>();
        public int DefaultRows=3;
        // Two-row groups use compact native buttons unless this is turned off.
        public bool CompactRibbon=true;
        public string Labels="icon";
        public bool PinTab=true;
        // With the pin on: clicking back into a view after using another tab returns to EK Custom.
        public bool ReturnOnClick=true;
        // "modify" (next to Modify), "first" or "default" (where Revit puts it).
        public string TabPosition="modify";

        internal static bool IsGap(string token) {return token!=null&&token.StartsWith(GapPrefix,StringComparison.Ordinal);}
        internal static string NewGap() {return GapPrefix+Guid.NewGuid().ToString("N").Substring(0,12);}
        internal bool IsDivider(string token) {return token!=null&&Dividers.Contains(token);}
        internal bool IsCommand(string token) {return token!=null&&Selected.Contains(token);}
        internal int RowsOf(string group)
        {
            return GroupRows.TryGetValue(group??StartGroup,out int n)&&(n==2||n==3)?n:DefaultRows;
        }
        internal int ZoomOf(string key) {return Zoom.TryGetValue(key,out int z)?z:100;}

        // ---------- persistence ----------

        internal Dictionary<string,object> ToJson()
        {
            return new Dictionary<string,object>{
                {"version",2},
                {"order",Order},{"selected",Selected.OrderBy(s=>s).ToArray()},
                {"dividers",Dividers.OrderBy(s=>s).ToArray()},{"group_rows",new SortedDictionary<string,int>(GroupRows,StringComparer.Ordinal)},
                {"individual_zoom",new SortedDictionary<string,int>(Zoom,StringComparer.Ordinal)},{"large",Large.OrderBy(s=>s).ToArray()},
                {"stack",DefaultRows==2?"two":"three"},{"labels",Labels},
                {"compact_ribbon",CompactRibbon},{"pin_tab",PinTab},{"return_on_click",ReturnOnClick},{"tab_position",TabPosition},
                {"commands",Definitions.Select(x=>x.ToJson()).ToArray()}};
        }

        internal static Layout FromJson(Dictionary<string,object> d)
        {
            var l=new Layout();
            l.Order.AddRange(Strings(Json.Get(d,"order")));
            l.Selected=new HashSet<string>(Strings(Json.Get(d,"selected")));
            l.Dividers=new HashSet<string>(Strings(Json.Get(d,"dividers")));
            l.Large=new HashSet<string>(Strings(Json.Get(d,"large")));
            l.GroupRows=IntMap(Json.Get(d,"group_rows"));
            l.Zoom=IntMap(Json.Get(d,"individual_zoom"));
            object compact=Json.Get(d,"compact_ribbon");
            l.CompactRibbon=compact==null||Json.Bool(compact);
            object pin=Json.Get(d,"pin_tab");
            l.PinTab=pin==null||Json.Bool(pin);
            object back=Json.Get(d,"return_on_click");
            l.ReturnOnClick=back==null||Json.Bool(back);
            string position=Json.Str(Json.Get(d,"tab_position"),"modify");
            l.TabPosition=position=="first"||position=="default"?position:"modify";
            string labels=Json.Str(Json.Get(d,"labels"),"icon");
            l.Labels=labels=="short"||labels=="full"?labels:"icon";
            l.DefaultRows=Json.Str(Json.Get(d,"stack"))=="two"?2:3;
            foreach(var row in Json.Arr(Json.Get(d,"commands")))
            {
                var ci=CommandInfo.FromJson(row);
                if(string.IsNullOrWhiteSpace(ci.Id))continue;
                ci.Key=CommandInfo.MakeKey(ci);
                l.Definitions.Add(ci);
            }
            // v1 stored "start this button on row N" separately; v2 stores explicit gaps.
            var rowPositions=IntMap(Json.Get(d,"row_positions"));
            if(rowPositions.Count>0)l.ApplyLegacyRowPositions(rowPositions);
            l.Normalize(null);
            return l;
        }

        internal Layout Clone() {return FromJson(Json.Obj(Json.Parse(Json.Dump(ToJson()))));}

        static IEnumerable<string> Strings(object a) {return Json.Arr(a).Select(x=>Json.Str(x)).Where(s=>s.Length>0);}
        static Dictionary<string,int> IntMap(object o)
        {
            var d=new Dictionary<string,int>();
            foreach(var kv in Json.Obj(o))d[kv.Key]=Json.Int(kv.Value,0);
            return d;
        }

        void ApplyLegacyRowPositions(Dictionary<string,int> rowPositions)
        {
            var result=new List<string>();
            int rows=RowsOf(StartGroup), filled=0;
            foreach(string token in Order)
            {
                if(Dividers.Contains(token)){result.Add(token);rows=RowsOf(token);filled=0;continue;}
                if(Selected.Contains(token))
                {
                    if(Large.Contains(token)){result.Add(token);filled=0;continue;}
                    if(rowPositions.TryGetValue(token,out int target))
                    {
                        target=Math.Max(0,Math.Min(rows-1,target));
                        while(filled%rows!=target){result.Add(NewGap());filled++;}
                    }
                }
                result.Add(token);
                filled++;
            }
            Order=result;
        }

        // ---------- arrangement (shared by the preview and the real ribbon) ----------

        // visible: which selected commands exist right now. Gaps always count.
        internal List<RibbonGroup> Arrange(Func<string,bool> visible)
        {
            var groups=new List<RibbonGroup>();
            var group=new RibbonGroup{Id=StartGroup,Rows=RowsOf(StartGroup)};
            groups.Add(group);
            var column=new List<string>();
            Action flush=()=>{
                if(column.Count==0)return;
                var cells=new string[group.Rows];
                for(int i=0;i<column.Count;i++)cells[i]=column[i];
                group.Columns.Add(new RibbonColumn{Cells=cells});
                column.Clear();
            };
            foreach(string token in Order)
            {
                if(Dividers.Contains(token))
                {
                    flush();
                    group=new RibbonGroup{Id=token,Rows=RowsOf(token)};
                    groups.Add(group);
                    continue;
                }
                bool gap=IsGap(token);
                if(!gap&&!(Selected.Contains(token)&&(visible==null||visible(token))))continue;
                if(!gap&&Large.Contains(token))
                {
                    flush();
                    group.Columns.Add(new RibbonColumn{Large=true,Cells=new[]{token}});
                    continue;
                }
                column.Add(token);
                if(column.Count==group.Rows)flush();
            }
            flush();
            return groups;
        }

        internal static CellRef Find(List<RibbonGroup> groups,string token)
        {
            for(int g=0;g<groups.Count;g++)
                for(int c=0;c<groups[g].Columns.Count;c++)
                {
                    var cells=groups[g].Columns[c].Cells;
                    for(int r=0;r<cells.Length;r++)
                        if(cells[r]==token)return new CellRef{Group=g,Column=c,Row=r};
                }
            return null;
        }

        internal string GroupOf(string token)
        {
            string group=StartGroup;
            foreach(string t in Order)
            {
                if(Dividers.Contains(t))group=t;
                if(t==token)return group;
            }
            return group;
        }

        // Tokens the user can select, in reading order (gaps excluded).
        internal List<string> Selectable(Func<string,bool> visible)
        {
            var result=new List<string>();
            foreach(var g in Arrange(visible))
            {
                if(g.Id!=StartGroup)result.Add(g.Id);
                foreach(var col in g.Columns)result.AddRange(col.Tokens.Where(t=>!IsGap(t)));
            }
            return result;
        }

        // ---------- editing ----------

        // Keeps the token list consistent and removes gaps that no longer shape anything.
        internal void Normalize(Func<string,bool> visible)
        {
            var seen=new HashSet<string>();
            Order=Order.Where(t=>(Dividers.Contains(t)||IsGap(t)||Selected.Contains(t))&&seen.Add(t)).ToList();
            Dividers.RemoveWhere(t=>!seen.Contains(t));
            foreach(string key in Selected.OrderBy(k=>k))if(seen.Add(key))Order.Add(key);
            for(int guard=0;guard<200;guard++)
            {
                var remove=new HashSet<string>();
                foreach(var g in Arrange(visible))
                {
                    for(int c=0;c<g.Columns.Count&&remove.Count==0;c++)
                    {
                        var col=g.Columns[c];
                        if(col.Large)continue;
                        var tokens=col.Tokens.ToList();
                        // An all-gap column is collapsed.
                        if(tokens.All(IsGap)){foreach(var t in tokens)remove.Add(t);break;}
                        // Trailing gaps only matter when another column follows in the same stack run.
                        bool lastInRun=c==g.Columns.Count-1||g.Columns[c+1].Large;
                        if(!lastInRun)continue;
                        for(int i=tokens.Count-1;i>=0&&IsGap(tokens[i]);i--)remove.Add(tokens[i]);
                    }
                    if(remove.Count>0)break;
                }
                if(remove.Count==0)break;
                Order.RemoveAll(remove.Contains);
            }
            Large.RemoveWhere(k=>!Selected.Contains(k));
            foreach(var k in Zoom.Keys.Where(k=>!Selected.Contains(k)||Zoom[k]==100).ToList())Zoom.Remove(k);
            foreach(var k in GroupRows.Keys.Where(k=>k!=StartGroup&&!Dividers.Contains(k)).ToList())GroupRows.Remove(k);
        }

        void Swap(string a,string b)
        {
            int i=Order.IndexOf(a),j=Order.IndexOf(b);
            Order[i]=b;Order[j]=a;
        }
        void ReplaceWithGap(string token)
        {
            int i=Order.IndexOf(token);
            if(i>=0)Order[i]=NewGap();
        }
        static IEnumerable<string> Gaps(int count)
        {
            for(int i=0;i<count;i++)yield return NewGap();
        }
        int LastIndex(RibbonColumn col) {return col.Tokens.Max(t=>Order.IndexOf(t));}
        int FirstIndex(RibbonColumn col) {return col.Tokens.Min(t=>Order.IndexOf(t));}
        int GroupEndIndex(List<RibbonGroup> groups,int g)
        {
            return g+1<groups.Count?Order.IndexOf(groups[g+1].Id):Order.Count;
        }

        // Arrow-key move. Small buttons move cell by cell on the visual grid,
        // swapping with whatever is there; large buttons move column by column.
        internal bool Move(string token,int dx,int dy,Func<string,bool> visible)
        {
            if(token==null||(dx==0&&dy==0))return false;
            bool changed=Dividers.Contains(token)?MoveDivider(token,dx,dy,visible):MoveButton(token,dx,dy,visible);
            if(changed)Normalize(visible);
            return changed;
        }

        bool MoveDivider(string token,int dx,int dy,Func<string,bool> visible)
        {
            if(dy!=0)return false;
            int at=Order.IndexOf(token);
            for(int i=at+dx;i>=0&&i<Order.Count;i+=dx)
            {
                string other=Order[i];
                if(Dividers.Contains(other))return false;
                if(IsGap(other)||(Selected.Contains(other)&&(visible==null||visible(other))))
                {Swap(token,other);return true;}
            }
            return false;
        }

        bool MoveButton(string token,int dx,int dy,Func<string,bool> visible)
        {
            var groups=Arrange(visible);
            var at=Find(groups,token);
            if(at==null)return false;
            var group=groups[at.Group];
            var col=group.Columns[at.Column];
            if(dy!=0)
            {
                if(col.Large)return false;
                int row=at.Row+dy;
                if(row<0||row>=group.Rows)return false;
                string target=col.Cells[row];
                if(target!=null){Swap(token,target);return true;}
                if(dy<0)return false;
                // Empty space below the last button of a short column: push it down.
                Order.Insert(Order.IndexOf(token),NewGap());
                return true;
            }
            int targetColumn=at.Column+dx;
            if(targetColumn<0)
            {
                if(at.Group==0)return false;
                string divider=group.Id;
                Order.Remove(token);
                Order.Insert(Order.IndexOf(divider),token);
                return true;
            }
            if(targetColumn>=group.Columns.Count)
            {
                bool alone=col.Large||col.Tokens.All(t=>t==token||IsGap(t));
                if(alone)
                {
                    if(at.Group+1>=groups.Count)return false;
                    string next=groups[at.Group+1].Id;
                    Order.Remove(token);
                    Order.Insert(Order.IndexOf(next)+1,token);
                    return true;
                }
                // Start a new column at the end of this group, keeping the row.
                int filled=Array.FindLastIndex(col.Cells,c=>c!=null)+1;
                ReplaceWithGap(token);
                var tail=Gaps(group.Rows-filled).Concat(Gaps(at.Row)).Concat(new[]{token});
                Order.InsertRange(GroupEndIndex(groups,at.Group),tail);
                return true;
            }
            var target2=group.Columns[targetColumn];
            if(col.Large)
            {
                string anchor=dx<0?Order[FirstIndex(target2)]:Order[LastIndex(target2)];
                Order.Remove(token);
                Order.Insert(Order.IndexOf(anchor)+(dx<0?0:1),token);
                return true;
            }
            if(target2.Large)
            {
                string large=target2.Cells[0];
                ReplaceWithGap(token);
                Order.Insert(Order.IndexOf(large)+(dx<0?0:1),token);
                return true;
            }
            string swapWith=target2.Cells[at.Row];
            if(swapWith!=null){Swap(token,swapWith);return true;}
            int used=Array.FindLastIndex(target2.Cells,c=>c!=null)+1;
            string last=Order[LastIndex(target2)];
            ReplaceWithGap(token);
            Order.InsertRange(Order.IndexOf(last)+1,Gaps(at.Row-used).Concat(new[]{token}));
            return true;
        }

        // Adds a command (if new) or moves an existing token so it takes the place of anchor.
        // Dropping on a gap fills that gap; dropping on a button pushes it along.
        internal bool PlaceBefore(string token,string anchor,Func<string,bool> visible)
        {
            if(token==null||anchor==null||token==anchor||!Order.Contains(anchor))return false;
            if(Dividers.Contains(token))
            {
                Order.Remove(token);
                Order.Insert(Order.IndexOf(anchor),token);
            }
            else
            {
                Selected.Add(token);
                Order.Remove(token);
                int i=Order.IndexOf(anchor);
                if(IsGap(anchor))Order[i]=token;else Order.Insert(i,token);
            }
            Normalize(visible);
            return true;
        }

        // Drop onto an empty cell of an existing column.
        internal bool PlaceAt(string token,int group,int column,int row,Func<string,bool> visible)
        {
            if(token==null||Dividers.Contains(token))return false;
            if(Order.Contains(token))ReplaceWithGap(token);
            Selected.Add(token);
            var groups=Arrange(visible);
            if(group>=groups.Count||column>=groups[group].Columns.Count)return AppendToGroup(token,group,visible);
            var col=groups[group].Columns[column];
            if(col.Large||row>=col.Cells.Length)return AppendToGroup(token,group,visible);
            if(col.Cells[row]!=null)return PlaceBefore(token,col.Cells[row],visible);
            int used=Array.FindLastIndex(col.Cells,c=>c!=null)+1;
            string last=Order[LastIndex(col)];
            Order.Remove(token);
            Order.InsertRange(Order.IndexOf(last)+1,Gaps(row-used).Concat(new[]{token}));
            Normalize(visible);
            return true;
        }

        internal bool AppendToGroup(string token,int group,Func<string,bool> visible)
        {
            if(token==null)return false;
            Order.Remove(token);
            if(!Dividers.Contains(token))Selected.Add(token);
            var groups=Arrange(visible);
            if(group<0||group>=groups.Count)Order.Add(token);
            else Order.Insert(GroupEndIndex(groups,group),token);
            Normalize(visible);
            return true;
        }

        internal void Add(string key,string after,Func<string,bool> visible=null)
        {
            if(string.IsNullOrEmpty(key)||Selected.Contains(key))return;
            Selected.Add(key);
            int i=after==null?-1:Order.IndexOf(after);
            if(i<0)Order.Add(key);else Order.Insert(i+1,key);
            Normalize(visible);
        }

        internal void Remove(string token,Func<string,bool> visible=null)
        {
            if(token==null)return;
            if(Dividers.Remove(token))GroupRows.Remove(token);
            Selected.Remove(token);
            Order.Remove(token);
            Normalize(visible);
        }

        internal string AddDivider(string after,Func<string,bool> visible=null)
        {
            string token=DividerPrefix+Guid.NewGuid().ToString("N").Substring(0,12);
            Dividers.Add(token);
            int i=after==null?-1:Order.IndexOf(after);
            if(i<0)Order.Add(token);else Order.Insert(i+1,token);
            Normalize(visible);
            return token;
        }
    }
}
