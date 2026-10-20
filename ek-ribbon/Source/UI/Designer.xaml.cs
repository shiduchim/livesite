using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using UIApplication = Autodesk.Revit.UI.UIApplication;

namespace EKRibbon.UI
{
    // The Ribbon Builder window. Every edit goes through Edit(), which records undo,
    // repaints the preview and updates the inspector and library.
    public partial class Designer:Window
    {
        const string MoveFormat="EKRibbon.Token", AddFormat="EKRibbon.NewCommand";
        const int LibraryLimit=240, PanelHeight=78;

        readonly UIApplication ui;
        readonly int year;
        Layout layout;
        List<CommandInfo> catalog;
        Dictionary<string,CommandInfo> byKey;
        HashSet<string> local;
        string active;
        bool quiet;

        readonly List<string> undo=new List<string>(), redo=new List<string>();
        string saved, lastCoalesce;
        DateTime lastEditUtc;

        readonly Dictionary<string,Border> visuals=new Dictionary<string,Border>();
        readonly Dictionary<string,Border> tiles=new Dictionary<string,Border>();
        readonly DispatcherTimer searchDelay=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(220)};
        Point pressPoint;
        string pressToken;
        Border highlighted;
        Brush highlightedBackground, highlightedBorder;

        public Designer(UIApplication application,int revitYear)
        {
            InitializeComponent();
            ui=application;year=revitYear;
            Icon=Icons.App(32);
            AppIcon.Source=Icons.App(32);
            Subtitle.Text="Revit "+year+"   ·   one layout shared by Revit 2024 – 2027";
            Footer.Text="EK Ribbon Builder "+EKRibbon.Application.Version;
            layout=State.Load();
            // Re-read the live ribbon every time so removed add-ins drop out of the library.
            catalog=Scanner.CollapseDuplicateDiscoveries(Scanner.Scan(year,ui),layout.Selected);
            RebuildLookup();
            saved=Snapshot();
            Wire();
            BuildKeyHints();
            LoadSettings();
            FillTabFilter();
            RefreshLibrary();
            Refresh();
            Status(Scanner.LastSuccess?"Ready. Your tab is shown above exactly as Revit will build it."
                :"The live scan did not finish ("+Scanner.LastError+"). Showing the last saved command list.");
            Loaded+=(s,e)=>PreviewScroll.Focus();
        }

        // ---------- data ----------

        void RebuildLookup()
        {
            byKey=catalog.GroupBy(x=>x.Key).ToDictionary(g=>g.Key,g=>g.First());
            local=new HashSet<string>(byKey.Keys);
            // Buttons saved from other Revit years stay visible in the preview.
            foreach(var ci in State.ReadForBuild(year,layout))
                if(layout.Selected.Contains(ci.Key)&&!byKey.ContainsKey(ci.Key))byKey[ci.Key]=ci;
        }

        bool Visible(string key) {return byKey.ContainsKey(key);}
        string NameOf(string key) {return byKey.TryGetValue(key??"",out var ci)?(ci.Name??"").Trim():"button";}
        string Snapshot() {return Json.Dump(layout.ToJson());}

        void Restore(string snapshot)
        {
            // Tab behaviour is applied live, so undo never rewinds it.
            bool pin=layout.PinTab, back=layout.ReturnOnClick;string position=layout.TabPosition;
            layout=Layout.FromJson(Json.Obj(Json.Parse(snapshot)));
            layout.PinTab=pin;layout.ReturnOnClick=back;layout.TabPosition=position;
        }

        bool Edit(Func<bool> change,string message,string coalesce=null)
        {
            string before=Snapshot();
            bool changed;
            try{changed=change();}
            catch(Exception ex){Log.Error("Ribbon Builder edit",ex);Status("That change failed: "+ex.Message);return false;}
            if(!changed||Snapshot()==before)return false;
            bool merge=coalesce!=null&&coalesce==lastCoalesce&&(DateTime.UtcNow-lastEditUtc).TotalSeconds<2;
            if(!merge)
            {
                undo.Add(before);
                if(undo.Count>200)undo.RemoveAt(0);
            }
            redo.Clear();
            lastCoalesce=coalesce;lastEditUtc=DateTime.UtcNow;
            Refresh();
            if(message!=null)Status(message);
            return true;
        }

        void Undo()
        {
            if(undo.Count==0){Status("Nothing to undo.");return;}
            redo.Add(Snapshot());
            Restore(undo[undo.Count-1]);
            undo.RemoveAt(undo.Count-1);
            lastCoalesce=null;
            LoadSettings();Refresh();
            Status("Undone.");
        }

        void Redo()
        {
            if(redo.Count==0){Status("Nothing to redo.");return;}
            undo.Add(Snapshot());
            Restore(redo[redo.Count-1]);
            redo.RemoveAt(redo.Count-1);
            lastCoalesce=null;
            LoadSettings();Refresh();
            Status("Redone.");
        }

        void Save()
        {
            try
            {
                State.UpdateDefinitions(year,layout,catalog);
                State.Save(layout);
                saved=Snapshot();
                UpdateDirty();
                Status("Saved. Restart Revit to see the new EK Custom tab (Revit can't rebuild a tab while it is running).");
            }
            catch(Exception ex)
            {
                Log.Error("Save layout",ex);
                MessageBox.Show(this,"The layout could not be saved:\n"+ex.Message,"EK Ribbon Builder",MessageBoxButton.OK,MessageBoxImage.Error);
            }
        }

        // Applies a setting to Revit now and to the saved file now, outside undo.
        void Instant(Action<Layout> apply,string message)
        {
            apply(layout);
            var onDisk=Layout.FromJson(Json.Obj(Json.Parse(saved)));
            apply(onDisk);
            saved=Json.Dump(onDisk.ToJson());
            try{State.Update(apply);}catch(Exception ex){Log.Error("Save setting",ex);}
            UpdateDirty();
            Status(message);
        }

        // ---------- wiring ----------

        void Wire()
        {
            UndoButton.Click+=(s,e)=>Undo();
            RedoButton.Click+=(s,e)=>Redo();
            SaveButton.Click+=(s,e)=>Save();
            AddDividerButton.Click+=(s,e)=>AddDivider();
            DividerAfter.Click+=(s,e)=>AddDivider();
            RemoveButton.Click+=(s,e)=>RemoveActive();
            RemoveDivider.Click+=(s,e)=>RemoveActive();
            MoveUp.Click+=(s,e)=>MoveActive(0,-1);
            MoveDown.Click+=(s,e)=>MoveActive(0,1);
            MoveLeft.Click+=(s,e)=>MoveActive(-1,0);
            MoveRight.Click+=(s,e)=>MoveActive(1,0);
            DividerLeft.Click+=(s,e)=>MoveActive(-1,0);
            DividerRight.Click+=(s,e)=>MoveActive(1,0);
            SizeSmall.Checked+=(s,e)=>{if(!quiet)SetLarge(false);};
            SizeLarge.Checked+=(s,e)=>{if(!quiet)SetLarge(true);};
            Rows2.Checked+=(s,e)=>{if(!quiet)SetRows(layout.GroupOf(active),2);};
            Rows3.Checked+=(s,e)=>{if(!quiet)SetRows(layout.GroupOf(active),3);};
            DividerRows2.Checked+=(s,e)=>{if(!quiet)SetRows(active,2);};
            DividerRows3.Checked+=(s,e)=>{if(!quiet)SetRows(active,3);};
            ZoomSlider.ValueChanged+=(s,e)=>{if(!quiet)SetZoom((int)Math.Round(e.NewValue/5)*5);};
            ZoomReset.Click+=(s,e)=>SetZoom(100);
            PinSwitch.Checked+=(s,e)=>SetPin(true);
            PinSwitch.Unchecked+=(s,e)=>SetPin(false);
            ReturnSwitch.Checked+=(s,e)=>SetReturnOnClick(true);
            ReturnSwitch.Unchecked+=(s,e)=>SetReturnOnClick(false);
            TabPositionBox.SelectionChanged+=(s,e)=>{
                if(quiet||!(TabPositionBox.SelectedItem is ComboBoxItem item))return;
                string position=(string)item.Tag;
                Instant(l=>l.TabPosition=position,"Tab position changed.");
                TabPin.SetPosition(position);
            };
            LabelsBox.SelectionChanged+=(s,e)=>{
                if(quiet||!(LabelsBox.SelectedItem is ComboBoxItem item))return;
                string labels=(string)item.Tag;
                Edit(()=>{layout.Labels=labels;return true;},"Labels: "+item.Content+". Applies after a Revit restart.");
            };
            BigTwoRow.Checked+=(s,e)=>{if(!quiet)Edit(()=>{layout.CompactRibbon=false;return true;},"2-row groups will use bigger icons.");};
            BigTwoRow.Unchecked+=(s,e)=>{if(!quiet)Edit(()=>{layout.CompactRibbon=true;return true;},"2-row groups will use compact icons.");};
            RescanButton.Click+=(s,e)=>Rescan();
            searchDelay.Tick+=(s,e)=>{searchDelay.Stop();RefreshLibrary();};
            SearchBox.TextChanged+=(s,e)=>{searchDelay.Stop();searchDelay.Start();};
            TabFilter.SelectionChanged+=(s,e)=>{if(!quiet)RefreshLibrary();};
            AddedOnly.Checked+=(s,e)=>RefreshLibrary();
            AddedOnly.Unchecked+=(s,e)=>RefreshLibrary();
            PreviewKeyDown+=OnKey;
            Closing+=OnClosing;
            Closed+=(s,e)=>searchDelay.Stop();
            PreviewScroll.MouseLeftButtonDown+=(s,e)=>{PreviewScroll.Focus();};

            // Dragging a button back onto the library removes it.
            LibraryCard.DragOver+=(s,e)=>{
                bool remove=e.Data.GetDataPresent(MoveFormat);
                e.Effects=remove?DragDropEffects.Move:DragDropEffects.None;
                LibraryCard.BorderBrush=remove?(Brush)FindResource("DangerInk"):(Brush)FindResource("Line");
                e.Handled=true;
            };
            LibraryCard.DragLeave+=(s,e)=>LibraryCard.BorderBrush=(Brush)FindResource("Line");
            LibraryCard.Drop+=(s,e)=>{
                LibraryCard.BorderBrush=(Brush)FindResource("Line");
                if(!(e.Data.GetData(MoveFormat) is string token))return;
                e.Handled=true;
                Dispatcher.BeginInvoke(new Action(()=>{active=token;RemoveActive();}));
            };
        }

        void LoadSettings()
        {
            quiet=true;
            PinSwitch.IsChecked=layout.PinTab;
            ReturnSwitch.IsChecked=layout.ReturnOnClick;
            ReturnSwitch.IsEnabled=layout.PinTab;
            TabPositionBox.SelectedItem=TabPositionBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i=>(string)i.Tag==layout.TabPosition);
            LabelsBox.SelectedItem=LabelsBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i=>(string)i.Tag==layout.Labels);
            BigTwoRow.IsChecked=!layout.CompactRibbon;
            quiet=false;
        }

        void BuildKeyHints()
        {
            var hints=new[]{
                new[]{"← → ↑ ↓","move"},new[]{"Tab","next button"},new[]{"Del","remove"},new[]{"L","large / small"},
                new[]{"D","divider"},new[]{"+ −","icon zoom"},new[]{"Ctrl Z","undo"},new[]{"Ctrl S","save"}};
            foreach(var hint in hints)
            {
                var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,16,0)};
                foreach(string key in hint[0].Split(' '))
                    row.Children.Add(new Border{Style=(Style)FindResource("Key"),
                        Child=new TextBlock{Text=key,FontSize=11,HorizontalAlignment=HorizontalAlignment.Center}});
                row.Children.Add(new TextBlock{Text=hint[1],Foreground=(Brush)FindResource("Muted"),FontSize=11.5,
                    Margin=new Thickness(3,0,0,0),VerticalAlignment=VerticalAlignment.Center});
                KeyHints.Children.Add(row);
            }
        }

        void Status(string text) {StatusText.Text=text;}

        void UpdateDirty()
        {
            bool dirty=Snapshot()!=saved;
            DirtyBadge.Visibility=dirty?Visibility.Visible:Visibility.Collapsed;
            Title=dirty?"EK Ribbon Builder  •  unsaved":"EK Ribbon Builder";
            UndoButton.IsEnabled=undo.Count>0;
            RedoButton.IsEnabled=redo.Count>0;
        }

        void Refresh()
        {
            if(active!=null&&!layout.Order.Contains(active))active=null;
            RefreshPreview();
            UpdateTiles();
            UpdateInspector();
            UpdateDirty();
        }

        // ---------- editing actions ----------

        void Select(string token)
        {
            active=token;
            foreach(var v in visuals.Values)Paint(v);
            UpdateInspector();
            if(token!=null&&visuals.TryGetValue(token,out var visual))visual.BringIntoView();
        }

        void MoveActive(int dx,int dy)
        {
            if(active==null){Status("Select a button in the preview first.");return;}
            if(!Edit(()=>layout.Move(active,dx,dy,Visible),null))Status("It can't move any further that way.");
            else Status("Moved "+(layout.IsDivider(active)?"divider":NameOf(active))+".");
            if(active!=null&&visuals.TryGetValue(active,out var visual))visual.BringIntoView();
        }

        void SetLarge(bool large)
        {
            if(active==null||!layout.IsCommand(active))return;
            Edit(()=>{
                if(large)layout.Large.Add(active);else layout.Large.Remove(active);
                layout.Normalize(Visible);
                return true;
            },large?"Large button: it gets its own column.":"Small button: stacked with its neighbours.");
        }

        void ToggleLarge()
        {
            if(active!=null&&layout.IsCommand(active))SetLarge(!layout.Large.Contains(active));
        }

        void SetRows(string group,int rows)
        {
            if(group==null)return;
            Edit(()=>{layout.GroupRows[group]=rows;layout.Normalize(Visible);return true;},"Group set to "+rows+" rows.");
        }

        void SetZoom(int zoom)
        {
            if(active==null||!layout.IsCommand(active))return;
            zoom=Math.Max(50,Math.Min(175,zoom));
            string key=active;
            Edit(()=>{if(zoom==100)layout.Zoom.Remove(key);else layout.Zoom[key]=zoom;return true;},
                "Icon zoom "+zoom+"%.","zoom:"+key);
        }

        void SetPin(bool pinned)
        {
            if(quiet)return;
            Instant(l=>l.PinTab=pinned,pinned?"EK Custom is pinned: Revit will keep coming back to it.":"EK Custom is no longer pinned.");
            try{TabPin.SetPinned(pinned);}catch(Exception ex){Log.Error("Pin from Ribbon Builder",ex);}
            ReturnSwitch.IsEnabled=pinned;
            RefreshPreview();
        }

        void SetReturnOnClick(bool value)
        {
            if(quiet)return;
            Instant(l=>l.ReturnOnClick=value,value?"Clicking back into a view will return to EK Custom.":"Other tabs you open stay open until Revit switches tabs.");
            try{TabPin.SetReturnOnClick(value);}catch(Exception ex){Log.Error("Return on click from Ribbon Builder",ex);}
        }

        void AddDivider()
        {
            string after=active!=null&&layout.Order.Contains(active)?active:null;
            Edit(()=>{active=layout.AddDivider(after,Visible);return true;},"Divider added. Use ← and → to shift it.");
        }

        void RemoveActive()
        {
            if(active==null){Status("Select a button in the preview first.");return;}
            var order=layout.Selectable(Visible);
            int index=order.IndexOf(active);
            string token=active, name=layout.IsDivider(token)?"divider":NameOf(token);
            if(!Edit(()=>{layout.Remove(token,Visible);return true;},"Removed "+name+". Ctrl+Z brings it back."))return;
            order=layout.Selectable(Visible);
            Select(order.Count==0?null:order[Math.Max(0,Math.Min(index,order.Count-1))]);
        }

        void SelectNext(int direction)
        {
            var order=layout.Selectable(Visible);
            if(order.Count==0)return;
            int index=order.IndexOf(active);
            index=index<0?(direction>0?0:order.Count-1):Math.Max(0,Math.Min(order.Count-1,index+direction));
            Select(order[index]);
        }

        void ToggleFromLibrary(string key)
        {
            if(!byKey.TryGetValue(key,out var ci))return;
            if(layout.Selected.Contains(key))
            {
                if(active==key)active=null;
                Edit(()=>{layout.Remove(key,Visible);return true;},"Removed "+ci.Name+".");
                return;
            }
            if(!ci.Registered){Status(ci.Name+" can't be run as a Revit command, so it can't go on the tab.");return;}
            string after=active!=null&&layout.Order.Contains(active)?active:null;
            Edit(()=>{layout.Add(key,after,Visible);active=key;return true;},
                "Added "+ci.Name+(after==null?" at the end.":" after the selected button."));
            if(visuals.TryGetValue(key,out var visual))visual.BringIntoView();
        }

        // ---------- keyboard ----------

        void OnKey(object sender,KeyEventArgs e)
        {
            bool ctrl=(Keyboard.Modifiers&ModifierKeys.Control)!=0, shift=(Keyboard.Modifiers&ModifierKeys.Shift)!=0;
            var focused=Keyboard.FocusedElement as DependencyObject;
            bool typing=focused is TextBox||focused is ComboBox||focused is ComboBoxItem||focused is Slider;
            if(ctrl&&e.Key==Key.S){Save();e.Handled=true;return;}
            if(ctrl&&e.Key==Key.F){SearchBox.Focus();SearchBox.SelectAll();e.Handled=true;return;}
            if(typing)
            {
                if(e.Key==Key.Escape&&focused==SearchBox)
                {
                    if(SearchBox.Text.Length>0)SearchBox.Text="";else PreviewScroll.Focus();
                    e.Handled=true;
                }
                return;
            }
            if(ctrl&&(e.Key==Key.Y||(e.Key==Key.Z&&shift))){Redo();e.Handled=true;return;}
            if(ctrl&&e.Key==Key.Z){Undo();e.Handled=true;return;}
            if(ctrl||(Keyboard.Modifiers&ModifierKeys.Alt)!=0)return;
            bool inPreview=focused==null||focused==this||focused==PreviewScroll||(focused is Visual v&&v.IsDescendantOf(PreviewScroll));
            switch(e.Key)
            {
                case Key.Left:MoveActive(-1,0);break;
                case Key.Right:MoveActive(1,0);break;
                case Key.Up:MoveActive(0,-1);break;
                case Key.Down:MoveActive(0,1);break;
                case Key.Tab:
                    if(!inPreview)return;
                    SelectNext(shift?-1:1);break;
                case Key.Home:SelectNext(-10000);break;
                case Key.End:SelectNext(10000);break;
                case Key.Delete:case Key.Back:RemoveActive();break;
                case Key.L:ToggleLarge();break;
                case Key.D:AddDivider();break;
                case Key.OemPlus:case Key.Add:if(active!=null)SetZoom(layout.ZoomOf(active)+5);break;
                case Key.OemMinus:case Key.Subtract:if(active!=null)SetZoom(layout.ZoomOf(active)-5);break;
                case Key.D0:case Key.NumPad0:SetZoom(100);break;
                case Key.Escape:Select(null);break;
                default:return;
            }
            e.Handled=true;
        }

        void OnClosing(object sender,System.ComponentModel.CancelEventArgs e)
        {
            if(Snapshot()==saved)return;
            var answer=MessageBox.Show(this,"Save your changes to the EK Custom tab?","EK Ribbon Builder",
                MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
            if(answer==MessageBoxResult.Cancel)e.Cancel=true;
            else if(answer==MessageBoxResult.Yes)Save();
        }

        // ---------- ribbon preview ----------

        int Cell(int rows) {return rows==2&&!layout.CompactRibbon?38:26;}
        // Real ribbon sizes: 16 px stacked icons, 32 px large ones.
        int IconPixels(int rows) {return rows==2&&!layout.CompactRibbon?32:16;}

        string Caption(string name)
        {
            name=(name??"").Replace("\r"," ").Replace("\n"," ").Trim();
            return layout.Labels=="short"&&name.Length>15?name.Substring(0,15):name;
        }

        void RefreshPreview()
        {
            double offset=PreviewScroll.HorizontalOffset;
            PreviewHost.Children.Clear();
            visuals.Clear();
            highlighted=null;
            var groups=layout.Arrange(Visible);
            var content=new StackPanel{Orientation=Orientation.Horizontal,Height=PanelHeight,Margin=new Thickness(4,4,4,3)};
            bool any=false;
            for(int g=0;g<groups.Count;g++)
            {
                var group=groups[g];
                if(group.Id!=Layout.StartGroup)content.Children.Add(DividerVisual(group.Id));
                if(group.Columns.Count==0)
                {
                    if(g>0)content.Children.Add(DropZone(g,"Empty group: drop buttons here"));
                    continue;
                }
                any=true;
                var strip=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Background=Brushes.Transparent};
                int groupIndex=g;
                AttachDrop(strip,null,t=>layout.AppendToGroup(t,groupIndex,Visible));
                for(int c=0;c<group.Columns.Count;c++)
                {
                    var column=group.Columns[c];
                    strip.Children.Add(column.Large?LargeVisual(column.Cells[0]):(UIElement)ColumnVisual(group,g,c));
                }
                content.Children.Add(strip);
            }
            if(!any)content.Children.Add(new TextBlock{Text="Your tab is empty. Click commands in the library below, or drag them here.",
                Foreground=(Brush)FindResource("Muted"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,16,0)});
            content.Children.Add(DropZone(groups.Count-1,"Drop here to add at the end"));
            PreviewHost.Children.Add(PanelFrame(RibbonBuilder.PanelName,content));
            PreviewHost.Children.Add(ToolsPreview());
            PreviewScroll.ScrollToHorizontalOffset(offset);
        }

        Border PanelFrame(string title,UIElement content)
        {
            var dock=new DockPanel();
            var caption=new Border{Background=(Brush)FindResource("PanelTitle"),CornerRadius=new CornerRadius(0,0,4,4),Padding=new Thickness(0,1,0,2),
                Child=new TextBlock{Text=title,FontSize=10.5,Foreground=(Brush)FindResource("Muted"),HorizontalAlignment=HorizontalAlignment.Center}};
            DockPanel.SetDock(caption,Dock.Bottom);
            dock.Children.Add(caption);
            dock.Children.Add(content);
            return new Border{Background=(Brush)FindResource("PanelBg"),BorderBrush=(Brush)FindResource("LineStrong"),
                BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(4),Margin=new Thickness(0,0,6,0),Child=dock};
        }

        // The pin and Ribbon Builder buttons are always added at the end of the tab.
        Border ToolsPreview()
        {
            var row=new StackPanel{Orientation=Orientation.Horizontal,Height=PanelHeight,Margin=new Thickness(4,4,4,3)};
            foreach(var icon in new[]{Icons.Pin(layout.PinTab,32),Icons.App(32)})
            {
                var image=new Image{Source=icon,Width=32,Height=32,Margin=new Thickness(6,0,6,0),VerticalAlignment=VerticalAlignment.Center};
                row.Children.Add(image);
            }
            var frame=PanelFrame(RibbonBuilder.ToolsPanelName,row);
            frame.Opacity=.85;
            frame.ToolTip="Always at the end of EK Custom: the pin, and this Ribbon Builder.";
            return frame;
        }

        Grid ColumnVisual(RibbonGroup group,int g,int c)
        {
            int rows=group.Rows, cell=Cell(rows);
            var grid=new Grid{VerticalAlignment=VerticalAlignment.Center};
            for(int r=0;r<rows;r++)grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(cell)});
            var cells=group.Columns[c].Cells;
            for(int r=0;r<rows;r++)
            {
                string token=cells[r];
                FrameworkElement element=token!=null&&!Layout.IsGap(token)?SmallVisual(token,rows):EmptyCell(token,g,c,r,cell);
                Grid.SetRow(element,r);
                grid.Children.Add(element);
            }
            return grid;
        }

        Border SmallVisual(string token,int rows)
        {
            var ci=byKey[token];
            int cell=Cell(rows), px=IconPixels(rows);
            FrameworkElement content=IconImage(ci,px);
            if(layout.Labels!="icon")
            {
                var row=new StackPanel{Orientation=Orientation.Horizontal};
                row.Children.Add(content);
                row.Children.Add(new TextBlock{Text=Caption(ci.Name),FontSize=11,Margin=new Thickness(4,0,3,0),
                    VerticalAlignment=VerticalAlignment.Center,MaxWidth=120,TextTrimming=TextTrimming.CharacterEllipsis});
                content=row;
            }
            var shell=Shell(token,content);
            shell.Height=cell-2;shell.MinWidth=cell-2;
            shell.HorizontalAlignment=HorizontalAlignment.Left;
            return shell;
        }

        Border LargeVisual(string token)
        {
            var ci=byKey[token];
            var stack=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
            var image=IconImage(ci,32);
            image.HorizontalAlignment=HorizontalAlignment.Center;
            stack.Children.Add(image);
            if(layout.Labels!="icon")
                stack.Children.Add(new TextBlock{Text=Caption(ci.Name),FontSize=11,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,
                    MaxWidth=64,MaxHeight=30,Margin=new Thickness(2,2,2,0),TextTrimming=TextTrimming.CharacterEllipsis});
            var shell=Shell(token,stack);
            shell.Width=layout.Labels=="icon"?44:68;
            shell.Height=PanelHeight-4;
            shell.VerticalAlignment=VerticalAlignment.Center;
            return shell;
        }

        Image IconImage(CommandInfo ci,int px)
        {
            var image=new Image{Source=Icons.Command(ci,px,layout.ZoomOf(ci.Key)),Width=px,Height=px,
                VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
            RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);
            if(!local.Contains(ci.Key))image.Opacity=.35;
            return image;
        }

        // A selectable, draggable, droppable button in the preview.
        Border Shell(string token,UIElement content)
        {
            var ci=byKey[token];
            var shell=new Border{Child=content,Tag=token,CornerRadius=new CornerRadius(4),BorderThickness=new Thickness(1.5),
                Margin=new Thickness(1),Padding=new Thickness(layout.Labels=="icon"?0:3,0,3,0),Cursor=Cursors.Hand,
                ToolTip=ci.Name+"\n"+ci.Tab+" › "+ci.Panel+(local.Contains(token)?"":"\nNot found in Revit "+year+" (kept for other versions)")};
            visuals[token]=shell;
            Paint(shell);
            shell.MouseEnter+=(s,e)=>{if(token!=active&&highlighted!=shell)shell.Background=(Brush)FindResource("Hover");};
            shell.MouseLeave+=(s,e)=>{if(highlighted!=shell)Paint(shell);};
            shell.MouseLeftButtonDown+=(s,e)=>{
                PreviewScroll.Focus();
                if(e.ClickCount==2){Select(token);ToggleLarge();e.Handled=true;return;}
                Select(token);
                pressPoint=e.GetPosition(this);pressToken=token;
                e.Handled=true;
            };
            shell.MouseLeftButtonUp+=(s,e)=>pressToken=null;
            shell.MouseMove+=(s,e)=>DragIfMoved(shell,token,e,MoveFormat);
            shell.MouseRightButtonUp+=(s,e)=>{Select(token);ContextMenuFor(token).IsOpen=true;e.Handled=true;};
            AttachDrop(shell,shell,t=>layout.PlaceBefore(t,token,Visible));
            return shell;
        }

        Border DividerVisual(string token)
        {
            var line=new Border{Width=2,Margin=new Thickness(0,6,0,6),CornerRadius=new CornerRadius(1)};
            var shell=new Border{Width=14,Tag=token,Child=line,Cursor=Cursors.SizeWE,CornerRadius=new CornerRadius(4),
                Margin=new Thickness(2,0,2,0),BorderThickness=new Thickness(1.5),
                ToolTip="Divider: drag it, or select it and use ← →. Buttons after it form a new group."};
            visuals[token]=shell;
            Paint(shell);
            shell.MouseLeftButtonDown+=(s,e)=>{
                PreviewScroll.Focus();Select(token);
                pressPoint=e.GetPosition(this);pressToken=token;e.Handled=true;
            };
            shell.MouseLeftButtonUp+=(s,e)=>pressToken=null;
            shell.MouseMove+=(s,e)=>DragIfMoved(shell,token,e,MoveFormat);
            shell.MouseRightButtonUp+=(s,e)=>{Select(token);ContextMenuFor(token).IsOpen=true;e.Handled=true;};
            AttachDrop(shell,shell,t=>layout.PlaceBefore(t,token,Visible));
            return shell;
        }

        // Empty cell of a column: drop a button exactly here. Gaps you made show dashed.
        FrameworkElement EmptyCell(string gap,int g,int c,int r,int cell)
        {
            var target=new Border{Width=cell-2,Height=cell-2,Margin=new Thickness(1),CornerRadius=new CornerRadius(4),
                Background=Brushes.Transparent,BorderThickness=new Thickness(1.5),BorderBrush=Brushes.Transparent,HorizontalAlignment=HorizontalAlignment.Left};
            if(gap!=null)target.Child=new Rectangle{Stroke=(Brush)FindResource("LineStrong"),StrokeDashArray=new DoubleCollection{3,2},
                RadiusX=3,RadiusY=3,Margin=new Thickness(3),ToolTip="Empty spot"};
            AttachDrop(target,target,t=>gap!=null?layout.PlaceBefore(t,gap,Visible):layout.PlaceAt(t,g,c,r,Visible));
            return target;
        }

        Border DropZone(int group,string tip)
        {
            var zone=new Border{Width=28,Margin=new Thickness(4,4,0,4),CornerRadius=new CornerRadius(5),BorderThickness=new Thickness(1.5),
                BorderBrush=Brushes.Transparent,Background=Brushes.Transparent,ToolTip=tip,
                Child=new Grid{Children={
                    new Rectangle{Stroke=(Brush)FindResource("LineStrong"),StrokeDashArray=new DoubleCollection{3,3},RadiusX=4,RadiusY=4},
                    new TextBlock{Text="+",FontSize=15,Foreground=(Brush)FindResource("Muted"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}}}};
            AttachDrop(zone,zone,t=>layout.AppendToGroup(t,group,Visible));
            return zone;
        }

        void Paint(Border shell)
        {
            string token=shell.Tag as string;
            bool selected=token!=null&&token==active;
            if(layout.IsDivider(token))
            {
                ((Border)shell.Child).Background=selected?(Brush)FindResource("Accent"):(Brush)FindResource("LineStrong");
                ((Border)shell.Child).Width=selected?3:2;
            }
            shell.Background=selected?(Brush)FindResource("AccentSoft"):Brushes.Transparent;
            shell.BorderBrush=selected?(Brush)FindResource("Accent"):Brushes.Transparent;
        }

        ContextMenu ContextMenuFor(string token)
        {
            var menu=new ContextMenu();
            Func<string,Action,MenuItem> item=(text,action)=>{var m=new MenuItem{Header=text};m.Click+=(s,e)=>action();return m;};
            if(layout.IsCommand(token))
            {
                menu.Items.Add(item(layout.Large.Contains(token)?"Make small   (L)":"Make large   (L)",ToggleLarge));
                menu.Items.Add(item("Reset icon zoom   (0)",()=>SetZoom(100)));
                menu.Items.Add(item("Add divider after   (D)",AddDivider));
                menu.Items.Add(new Separator());
                menu.Items.Add(item("Remove   (Del)",RemoveActive));
            }
            else
            {
                menu.Items.Add(item("Shift left   (←)",()=>MoveActive(-1,0)));
                menu.Items.Add(item("Shift right   (→)",()=>MoveActive(1,0)));
                menu.Items.Add(new Separator());
                menu.Items.Add(item("Remove divider   (Del)",RemoveActive));
            }
            return menu;
        }

        // ---------- drag and drop ----------

        void DragIfMoved(FrameworkElement source,string token,MouseEventArgs e,string format)
        {
            if(pressToken!=token||e.LeftButton!=MouseButtonState.Pressed)return;
            var p=e.GetPosition(this);
            if(Math.Abs(p.X-pressPoint.X)+Math.Abs(p.Y-pressPoint.Y)<7)return;
            pressToken=null;
            try{DragDrop.DoDragDrop(source,new DataObject(format,token),format==AddFormat?DragDropEffects.Copy:DragDropEffects.Move);}
            catch(Exception ex){Log.Error("Drag",ex);}
            finally{ClearHighlight();}
        }

        static string Dragged(DragEventArgs e)
        {
            return e.Data.GetData(MoveFormat) as string??e.Data.GetData(AddFormat) as string;
        }

        void AttachDrop(FrameworkElement target,Border highlight,Func<string,bool> place)
        {
            target.AllowDrop=true;
            DragEventHandler over=(s,e)=>{
                string token=Dragged(e);
                e.Effects=token==null?DragDropEffects.None:e.Data.GetDataPresent(AddFormat)?DragDropEffects.Copy:DragDropEffects.Move;
                if(token!=null&&highlight!=null)Highlight(highlight);
                e.Handled=true;
            };
            target.DragEnter+=over;
            target.DragOver+=over;
            target.DragLeave+=(s,e)=>{if(highlight!=null&&highlighted==highlight)ClearHighlight();};
            target.Drop+=(s,e)=>{
                string token=Dragged(e);
                ClearHighlight();
                if(token==null)return;
                e.Handled=true;
                bool added=e.Data.GetDataPresent(AddFormat);
                // Rebuild the preview after the drag operation has fully finished.
                Dispatcher.BeginInvoke(new Action(()=>{
                    if(Edit(()=>{bool ok=place(token);if(ok)active=token;return ok;},
                        added?"Added "+NameOf(token)+".":"Moved "+(layout.IsDivider(token)?"divider":NameOf(token))+"."))
                        Select(token);
                }));
            };
        }

        void Highlight(Border border)
        {
            if(highlighted==border)return;
            ClearHighlight();
            highlighted=border;
            highlightedBackground=border.Background;
            highlightedBorder=border.BorderBrush;
            border.Background=(Brush)FindResource("AccentSoft");
            border.BorderBrush=(Brush)FindResource("Accent");
        }

        void ClearHighlight()
        {
            if(highlighted==null)return;
            highlighted.Background=highlightedBackground;
            highlighted.BorderBrush=highlightedBorder;
            highlighted=null;
        }

        // ---------- inspector ----------

        void UpdateInspector()
        {
            quiet=true;
            bool divider=active!=null&&layout.IsDivider(active);
            bool button=active!=null&&!divider&&byKey.ContainsKey(active);
            NothingSelected.Visibility=!divider&&!button?Visibility.Visible:Visibility.Collapsed;
            DividerPanel.Visibility=divider?Visibility.Visible:Visibility.Collapsed;
            ButtonPanel.Visibility=button?Visibility.Visible:Visibility.Collapsed;
            if(divider)
            {
                DividerRows2.IsChecked=layout.RowsOf(active)==2;
                DividerRows3.IsChecked=layout.RowsOf(active)==3;
            }
            if(button)
            {
                var ci=byKey[active];
                int zoom=layout.ZoomOf(active);
                SelectedIcon.Source=Icons.Command(ci,32,zoom);
                SelectedName.Text=ci.Name;
                SelectedOrigin.Text=(ci.Tab??"")+"  ›  "+(ci.Panel??"");
                bool here=local.Contains(active);
                SelectedWarning.Visibility=here?Visibility.Collapsed:Visibility.Visible;
                SelectedWarning.Text="Not found in Revit "+year+". It stays on the tab in the Revit versions that have it.";
                SizeLarge.IsChecked=layout.Large.Contains(active);
                SizeSmall.IsChecked=!layout.Large.Contains(active);
                ZoomSlider.Value=zoom;
                ZoomValue.Text=zoom+"%";
                int rows=layout.RowsOf(layout.GroupOf(active));
                Rows2.IsChecked=rows==2;
                Rows3.IsChecked=rows==3;
                Rows2.IsEnabled=Rows3.IsEnabled=!layout.Large.Contains(active);
            }
            quiet=false;
        }

        // ---------- library ----------

        void FillTabFilter()
        {
            quiet=true;
            string previous=TabFilter.SelectedItem as string;
            TabFilter.Items.Clear();
            TabFilter.Items.Add("All tabs");
            foreach(string tab in catalog.Select(x=>x.Tab).Where(t=>!string.IsNullOrWhiteSpace(t)).Distinct().OrderBy(t=>t))TabFilter.Items.Add(tab);
            TabFilter.SelectedItem=previous!=null&&TabFilter.Items.Contains(previous)?previous:"All tabs";
            quiet=false;
        }

        void RefreshLibrary()
        {
            tiles.Clear();
            LibraryHost.Children.Clear();
            string tab=TabFilter.SelectedItem as string??"All tabs";
            var words=(SearchBox.Text??"").Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries);
            bool addedOnly=AddedOnly.IsChecked==true;
            var results=catalog.Where(x=>(tab=="All tabs"||x.Tab==tab)&&(!addedOnly||layout.Selected.Contains(x.Key))&&
                    words.All(w=>((x.Name??"")+" "+(x.Tab??"")+" "+(x.Panel??"")+" "+(x.Id??"")).IndexOf(w,StringComparison.OrdinalIgnoreCase)>=0))
                .OrderBy(x=>x.Tab).ThenBy(x=>x.Panel).ThenBy(x=>x.Name).ToList();
            foreach(var group in results.Take(LibraryLimit).GroupBy(x=>(x.Tab??"")+"\u0001"+(x.Panel??"")))
            {
                var first=group.First();
                var heading=new TextBlock{Margin=new Thickness(2,8,0,4),FontSize=11};
                heading.Inlines.Add(new System.Windows.Documents.Run(first.Tab){FontWeight=FontWeights.SemiBold});
                heading.Inlines.Add(new System.Windows.Documents.Run("  ›  "+first.Panel){Foreground=(Brush)FindResource("Muted")});
                LibraryHost.Children.Add(heading);
                var wrap=new WrapPanel();
                foreach(var ci in group)wrap.Children.Add(Tile(ci));
                LibraryHost.Children.Add(wrap);
            }
            LibraryStatus.Text=results.Count==0?"No matching commands. Try another word, another tab, or Rescan Revit."
                :results.Count>LibraryLimit?"Showing "+LibraryLimit+" of "+results.Count+". Type in the search box to narrow it down."
                :results.Count+" command"+(results.Count==1?"":"s")+". Click to add or remove, or drag onto your tab.";
            UpdateTiles();
            LibraryScroll.ScrollToTop();
        }

        Border Tile(CommandInfo ci)
        {
            string key=ci.Key;
            var badge=new Border{Width=15,Height=15,CornerRadius=new CornerRadius(7.5),Background=(Brush)FindResource("Accent"),
                HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,4,4,0),
                Child=new TextBlock{Text="\uE73E",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=8,Foreground=Brushes.White,
                    HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
            var image=new Image{Source=Icons.Command(ci,24),Width=24,Height=24};
            RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);
            var body=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
            body.Children.Add(image);
            body.Children.Add(new TextBlock{Text=(ci.Name??"").Replace("\n"," "),FontSize=10.5,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,
                MaxHeight=28,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(4,4,4,0)});
            var tile=new Border{Width=84,Height=66,CornerRadius=new CornerRadius(6),Margin=new Thickness(0,0,6,6),BorderThickness=new Thickness(1),
                Tag=badge,Cursor=Cursors.Hand,Child=new Grid{Children={body,badge}},
                ToolTip=ci.Name+"\n"+ci.Tab+" › "+ci.Panel+(ci.Registered?"":"\nCan't be run as a command, so it can't go on the tab.")};
            tiles[key]=tile;
            tile.MouseEnter+=(s,e)=>PaintTile(key,true);
            tile.MouseLeave+=(s,e)=>PaintTile(key,false);
            tile.MouseLeftButtonDown+=(s,e)=>{pressPoint=e.GetPosition(this);pressToken=key;e.Handled=true;};
            tile.MouseLeftButtonUp+=(s,e)=>{if(pressToken==key){pressToken=null;ToggleFromLibrary(key);}};
            tile.MouseMove+=(s,e)=>{
                if(!ci.Registered&&!layout.Selected.Contains(key))return;
                DragIfMoved(tile,key,e,layout.Selected.Contains(key)?MoveFormat:AddFormat);
            };
            return tile;
        }

        void PaintTile(string key,bool hover)
        {
            if(!tiles.TryGetValue(key,out var tile))return;
            bool added=layout.Selected.Contains(key);
            bool usable=added||(byKey.TryGetValue(key,out var ci)&&ci.Registered);
            ((Border)tile.Tag).Visibility=added?Visibility.Visible:Visibility.Collapsed;
            tile.Opacity=usable?1:.45;
            tile.Background=added?(Brush)FindResource("AccentSoft"):hover&&usable?(Brush)FindResource("Hover"):(Brush)FindResource("CardBg");
            tile.BorderBrush=added?(Brush)FindResource("Accent"):hover&&usable?(Brush)FindResource("LineStrong"):(Brush)FindResource("Line");
        }

        void UpdateTiles()
        {
            foreach(string key in tiles.Keys)PaintTile(key,false);
            LibraryCount.Text=catalog.Count+" commands found in Revit "+year+"   ·   "+layout.Selected.Count+" on your tab";
        }

        // ---------- rescan ----------

        void Rescan()
        {
            Cursor=Cursors.Wait;
            Status("Scanning every Revit tab…");
            try
            {
                catalog=Scanner.CollapseDuplicateDiscoveries(Scanner.Scan(year,ui,true),layout.Selected);
                RebuildLookup();FillTabFilter();RefreshLibrary();Refresh();
            }
            finally{Cursor=null;}
            if(!Scanner.LastSuccess)
            {
                Status("The scan did not finish: "+Scanner.LastError+". The previous command list was kept.");
                return;
            }
            var plan=SmartRescan.Plan(year,layout,catalog.Concat(Scanner.LastLiveExternal),Scanner.LastLiveExternal);
            int swaps=plan.Changes.Count, updates=plan.MetadataUpdates.Count;
            if(swaps==0&&updates==0)
            {
                Status("Scan complete: "+catalog.Count+" commands. "+(plan.Missing.Count>0
                    ?plan.Missing.Count+" saved button(s) aren't installed in Revit "+year+"; they stay on the tab for other versions."
                    :"Every saved button was found."));
                return;
            }
            string list=string.Join("\n",plan.Changes.Take(15).Select(x=>"   "+x.Previous.Tab+" › "+x.Previous.Name+"   →   "+x.Replacement.Tab+" › "+x.Replacement.Name));
            if(swaps>15)list+="\n   …and "+(swaps-15)+" more";
            string prompt="Rescan found changes to buttons on your tab:\n\n"+
                (swaps>0?swaps+" button(s) whose add-in was replaced by an installed C# version:\n"+list+"\n\n":"")+
                (updates>0?updates+" button(s) with a new icon or name.\n\n":"")+
                (plan.Skipped.Count>0?"Left alone because the match was uncertain:\n   "+string.Join("\n   ",plan.Skipped.Take(5))+"\n\n":"")+
                "Update them? Positions, dividers, rows and zoom are kept, and a backup is made first. "+
                "This saves the layout, including any other unsaved changes.";
            if(MessageBox.Show(this,prompt,"Review add-in changes",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)
            {
                Status("Scan complete. Suggested updates were not applied.");
                return;
            }
            try
            {
                layout=SmartRescan.ApplyAndSave(year,layout,plan,catalog.Concat(Scanner.LastLiveExternal));
                catalog=Scanner.CollapseDuplicateDiscoveries(catalog.Concat(Scanner.LastLiveExternal),layout.Selected);
                saved=Snapshot();
                undo.Clear();redo.Clear();
                RebuildLookup();FillTabFilter();RefreshLibrary();Refresh();
                Status("Updated "+swaps+" button(s) and "+updates+" icon/name(s), and saved. Restart Revit to see them.");
            }
            catch(Exception ex)
            {
                Log.Error("Smart Rescan",ex);
                MessageBox.Show(this,"The updates could not be saved; your layout was not changed.\n"+ex.Message,"Rescan",MessageBoxButton.OK,MessageBoxImage.Error);
            }
        }
    }
}
