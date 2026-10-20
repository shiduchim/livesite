using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace EKRibbon.Tests
{
    public class LayoutTests
    {
        static Layout Make(int rows,params string[] tokens)
        {
            var l=new Layout{DefaultRows=rows};
            foreach(var t in tokens)
            {
                if(t=="|"){l.AddDivider(null);continue;}
                if(t=="_"){l.Order.Add(Layout.NewGap());continue;}
                string key=t.TrimEnd('!');
                l.Selected.Add(key);l.Order.Add(key);
                if(t.EndsWith("!"))l.Large.Add(key);
            }
            return l;
        }

        // Renders the arrangement as text: groups split by " | ", columns by " ",
        // large columns as [X], empty cells as "." and gaps as "_".
        static string Picture(Layout l)
        {
            var groups=l.Arrange(null);
            return string.Join(" | ",groups.Select(g=>string.Join(" ",g.Columns.Select(c=>
                c.Large?"["+c.Cells[0]+"]":string.Concat(c.Cells.Select(x=>x==null?".":Layout.IsGap(x)?"_":x))))));
        }

        [Fact]
        public void ArrangesColumnMajorWithLargeColumnsFlushing()
        {
            var l=Make(3,"A","B","C","D","L!","E");
            Assert.Equal("ABC D.. [L] E..",Picture(l));
        }

        [Fact]
        public void DividersStartNewGroupsWithTheirOwnRowCount()
        {
            var l=Make(3,"A","B","|","C","D","E");
            l.GroupRows[l.GroupOf("C")]=2;
            Assert.Equal("AB. | CD E.",Picture(l));
        }

        [Fact]
        public void ArrowDownSwapsWithButtonBelow()
        {
            var l=Make(3,"A","B","C");
            Assert.True(l.Move("A",0,1,null));
            Assert.Equal("BAC",Picture(l));
        }

        [Fact]
        public void ArrowDownIntoEmptyCellLeavesGapAbove()
        {
            var l=Make(3,"A","B","C","D");
            Assert.True(l.Move("D",0,1,null));
            Assert.Equal("ABC _D.",Picture(l));
            Assert.True(l.Move("D",0,1,null));
            Assert.Equal("ABC __D",Picture(l));
            Assert.False(l.Move("D",0,1,null));
            Assert.True(l.Move("D",0,-1,null));
            Assert.Equal("ABC _D.",Picture(l));
            Assert.True(l.Move("D",0,-1,null));
            Assert.Equal("ABC D..",Picture(l));
        }

        [Fact]
        public void ArrowRightSwapsWithSameRowInNextColumn()
        {
            var l=Make(3,"A","B","C","D","E","F");
            Assert.True(l.Move("B",1,0,null));
            Assert.Equal("AEC DBF",Picture(l));
        }

        [Fact]
        public void ArrowRightIntoEmptyCellKeepsTheRow()
        {
            var l=Make(3,"A","B","C","D");
            Assert.True(l.Move("C",1,0,null));
            Assert.Equal("AB_ D_C",Picture(l));
        }

        [Fact]
        public void ArrowRightPastLastColumnOpensNewColumn()
        {
            var l=Make(3,"A","B","C");
            Assert.True(l.Move("B",1,0,null));
            Assert.Equal("A_C _B.",Picture(l));
            // Moving back collapses the extra column again.
            Assert.True(l.Move("B",-1,0,null));
            Assert.Equal("ABC",Picture(l));
        }

        [Fact]
        public void LoneButtonCrossesDividerIntoNextGroup()
        {
            var l=Make(3,"A","B","C","D","|","E");
            Assert.True(l.Move("D",1,0,null));
            Assert.Equal("ABC | DE.",Picture(l));
            Assert.True(l.Move("D",-1,0,null));
            Assert.Equal("ABC D.. | E..",Picture(l));
        }

        [Fact]
        public void LastGroupHasNowhereFurtherRight()
        {
            var l=Make(3,"A");
            Assert.False(l.Move("A",1,0,null));
            Assert.False(l.Move("A",-1,0,null));
            Assert.False(l.Move("A",0,-1,null));
        }

        [Fact]
        public void LargeButtonMovesWholeColumns()
        {
            var l=Make(3,"A","B","C","L!","D");
            Assert.True(l.Move("L",-1,0,null));
            Assert.Equal("[L] ABC D..",Picture(l));
            Assert.True(l.Move("L",1,0,null));
            Assert.Equal("ABC [L] D..",Picture(l));
            Assert.True(l.Move("L",1,0,null));
            Assert.Equal("ABC D.. [L]",Picture(l));
            Assert.False(l.Move("L",0,1,null));
        }

        [Fact]
        public void SmallButtonJumpsOverLargeButton()
        {
            var l=Make(3,"A","B","C","L!");
            Assert.True(l.Move("B",1,0,null));
            Assert.Equal("A_C [L] B..",Picture(l));
        }

        [Fact]
        public void DividerShiftsByOneButton()
        {
            var l=Make(3,"A","B","|","C");
            string divider=l.Dividers.Single();
            Assert.True(l.Move(divider,-1,0,null));
            Assert.Equal("A.. | BC.",Picture(l));
            Assert.True(l.Move(divider,1,0,null));
            Assert.Equal("AB. | C..",Picture(l));
        }

        [Fact]
        public void InvisibleCommandsAreSkippedButKept()
        {
            var l=Make(3,"A","X","B","C");
            System.Func<string,bool> visible=k=>k!="X";
            Assert.Equal("ABC",string.Concat(l.Arrange(visible)[0].Columns[0].Cells));
            Assert.True(l.Move("A",0,1,visible));
            Assert.Contains("X",l.Order);
            Assert.True(l.Selected.Contains("X"));
        }

        [Fact]
        public void PlaceBeforeInsertsAndPlaceOnGapFillsIt()
        {
            var l=Make(3,"A","_","C");
            Assert.Equal("A_C",Picture(l));
            string gap=l.Order[1];
            Assert.True(l.PlaceBefore("N",gap,null));
            Assert.Equal("ANC",Picture(l));
            Assert.True(l.PlaceBefore("C","A",null));
            Assert.Equal("CAN",Picture(l));
        }

        [Fact]
        public void PlaceAtEmptyCellPadsWithGaps()
        {
            var l=Make(3,"A","B","C","D");
            Assert.True(l.PlaceAt("N",0,1,2,null));
            Assert.Equal("ABC D_N",Picture(l));
            Assert.True(l.PlaceAt("A",0,1,1,null));
            Assert.Equal("_BC DAN",Picture(l));
        }

        [Fact]
        public void RemoveReflowsAndDropsSettings()
        {
            var l=Make(3,"A","B","C","D");
            l.Zoom["B"]=130;l.Large.Add("B");
            l.Remove("B");
            Assert.Equal("ACD",Picture(l));
            Assert.False(l.Zoom.ContainsKey("B"));
            Assert.False(l.Large.Contains("B"));
        }

        [Fact]
        public void SelectableListsDividersAndButtonsButNotGaps()
        {
            var l=Make(3,"A","_","B","|","C");
            var list=l.Selectable(null);
            Assert.Equal(new[]{"A","B",l.Dividers.Single(),"C"},list);
        }

        [Fact]
        public void JsonRoundTripPreservesEverything()
        {
            var l=Make(2,"A","_","B","|","L!");
            l.Zoom["A"]=120;l.Labels="short";l.PinTab=false;l.TabPosition="first";l.CompactRibbon=false;
            l.GroupRows[l.Dividers.Single()]=3;
            l.Definitions.Add(new CommandInfo{Id="ID_X",Name="X",Tab="T",Panel="P"});
            var copy=Layout.FromJson(Json.Obj(Json.Parse(Json.Dump(l.ToJson()))));
            Assert.Equal(Picture(l),Picture(copy));
            Assert.Equal(120,copy.ZoomOf("A"));
            Assert.Equal("short",copy.Labels);
            Assert.False(copy.PinTab);
            Assert.Equal("first",copy.TabPosition);
            Assert.False(copy.CompactRibbon);
            Assert.Equal(3,copy.RowsOf(copy.Dividers.Single()));
            Assert.Equal(CommandInfo.MakeKey(new CommandInfo{Id="ID_X"}),copy.Definitions.Single().Key);
        }

        [Fact]
        public void OldLayoutsLoadWithDefaultsAndRowPositionsBecomeGaps()
        {
            // A v1.2.1 shared layout: D was pinned to the third row.
            string v1="{\"order\":[\"A\",\"B\",\"C\",\"D\"],\"selected\":[\"A\",\"B\",\"C\",\"D\"],"+
                "\"dividers\":[],\"group_rows\":{},\"row_positions\":{\"D\":2},\"individual_zoom\":{\"A\":115},"+
                "\"large\":[],\"stack\":\"three\",\"frame_icons\":false,\"labels\":\"icon\",\"compact_ribbon\":true,"+
                "\"browser_grid\":true,\"browser_icon_size\":48,\"commands\":[]}";
            var l=Layout.FromJson(Json.Obj(Json.Parse(v1)));
            Assert.Equal("ABC __D",Picture(l));
            Assert.Equal(115,l.ZoomOf("A"));
            Assert.True(l.PinTab);
            Assert.Equal("modify",l.TabPosition);
        }

        [Fact]
        public void NormalizeRepairsOrderAndCollapsesEmptyColumns()
        {
            var l=Make(3,"A","B","C","_","_","_","D");
            l.Selected.Add("Z");
            l.Order.Add("ghost");
            l.Normalize(null);
            Assert.Equal("ABC DZ.",Picture(l));
            Assert.DoesNotContain("ghost",l.Order);
        }
    }
}
