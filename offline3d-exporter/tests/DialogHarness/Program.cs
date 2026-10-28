using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Offline3D.RevitExporter;

// Usage: DialogHarness <output folder>
// Drives the dialog through its states with a timer and saves screenshots.
string output = Path.GetFullPath(args.Length > 0 ? args[0] : "dialog-shots");
Directory.CreateDirectory(output);
Application.SetHighDpiMode(HighDpiMode.SystemAware);
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

var revit = new Form { Text = "Autodesk Revit (simulated)", BackColor = Color.FromArgb(214, 219, 226), StartPosition = FormStartPosition.Manual, Bounds = Screen.PrimaryScreen!.WorkingArea };
revit.Show();
Application.DoEvents();

string image = Path.Combine(output, "fake-preview.png");
using (var bitmap = new Bitmap(1200, 800))
using (var g = Graphics.FromImage(bitmap))
{
    g.Clear(Color.White); g.SmoothingMode = SmoothingMode.AntiAlias;
    var rnd = new Random(3);
    for (int i = 0; i < 40; i++)
    {
        int x = 150 + rnd.Next(800), y = 150 + rnd.Next(450), w = 40 + rnd.Next(160), h = 20 + rnd.Next(90);
        var top = new[] { new Point(x, y), new Point(x + w, y - w / 3), new Point(x + w + h, y - w / 3 + h / 3), new Point(x + h, y + h / 3) };
        using var brush = new SolidBrush(Color.FromArgb(150 + rnd.Next(80), 150 + rnd.Next(80), 160 + rnd.Next(80)));
        g.FillPolygon(brush, top); g.DrawPolygon(Pens.DimGray, top);
    }
    bitmap.Save(image);
}

void Shot(Form form, string name)
{
    form.Refresh(); Application.DoEvents();
    var bounds = form.Bounds;
    using var bmp = new Bitmap(bounds.Width, bounds.Height);
    try { using var g = Graphics.FromImage(bmp); g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size); }
    catch { form.DrawToBitmap(bmp, new Rectangle(Point.Empty, bounds.Size)); }
    bmp.Save(Path.Combine(output, name + ".png"));
    // Layout facts for the report: any horizontal scrollbar, clipped buttons.
    var scrollers = All(form).OfType<Panel>().Where(p => p.AutoScroll).ToList();
    var buttons = All(form).OfType<Button>().Where(b => b.Visible && b.FindForm() == form).ToList();
    var clipped = buttons.Where(b => { var r = form.RectangleToClient(b.RectangleToScreen(b.ClientRectangle)); return r.Right > form.ClientSize.Width || r.Bottom > form.ClientSize.Height; }).Select(b => b.Text);
    File.AppendAllText(Path.Combine(output, "layout.txt"), name + ": size " + form.Size + ", DPI " + form.DeviceDpi + ", horizontal scrollbar " + scrollers.Any(p => p.HorizontalScroll.Visible) + ", vertical scrollbar " + scrollers.Any(p => p.VerticalScroll.Visible) + ", buttons outside the window: [" + string.Join(", ", clipped) + "]" + Environment.NewLine);
}
static IEnumerable<Control> All(Control root) { foreach (Control c in root.Controls) { yield return c; foreach (var d in All(c)) yield return d; } }
T Find<T>(Form form, string text) where T : Control => All(form).OfType<T>().First(c => c.Text == text && c.Visible);

// No Revit ElementIds here: filters only need names (ids stay InvalidElementId).
NamedChoice F(string name) => new NamedChoice { Name = name };
var filters = new[] { F("None"), F("Show All"), F("Show Complete"), F("Show Demo + New"), F("Show New"), F("Show Previous + Demo"), F("Show Previous + New"), F("Show Previous Phase") };
foreach (var (palette, theme) in new[] { (EkPalette.Light, "light"), (EkPalette.DarkTheme, "dark") })
{
    var dialog = new ExportOptionsDialog(palette, "Level 2 - Lighting Plan (East wing)", "Level 1", "Level 3", 56200, true, filters, 2, 1, ExportDetail.Medium, true);
    var steps = new Queue<Action>(new Action[]
    {
        () => Shot(dialog, theme + "-01-initial"),
        () => Find<EkLink>(dialog, "Generate").PerformClick(),
        () => Shot(dialog, theme + "-03-preview-ready"),
        () => { All(dialog).OfType<EkDropDown>().First().SelectedIndex = 1; },
        () => Shot(dialog, theme + "-04-options-changed"),
        () => Find<EkDropDown>(dialog, "").PerformClick(),
        () => Shot(dialog, theme + "-05-dropdown"),
        () => Find<EkButton>(dialog, "Cancel").PerformClick(),
    });
    var timer = new System.Windows.Forms.Timer { Interval = 700 };
    timer.Tick += (_, __) => { if (steps.Count > 0) steps.Dequeue()(); else timer.Stop(); };
    timer.Start();
    var outcome = dialog.RunModal(revit, (request, report) =>
    {
        report("Scanning model elements… 12,000");
        Shot(dialog, theme + "-02-rendering");
        Thread.Sleep(300);
        return image;
    });
    timer.Stop();
    File.AppendAllText(Path.Combine(output, "layout.txt"), theme + " outcome: " + outcome + Environment.NewLine);

    // Progress window during export: geometry at 42 %.
    double clock = 0; var model = new ExportProgressModel(() => clock);
    model.AddRegion(10000, new Dictionary<string, long>()); model.BeginRegion();
    for (int second = 1; second <= 30; second++) { for (int i = 0; i < 140; i++) model.ElementBegun(false); clock = second; model.SetModelBytes(second * 1_400_000L); model.Update(); }
    var progress = new ExportProgressDialog(palette) { Model = model };
    progress.Show(revit);
    progress.Pump("Collecting model geometry", new ExportProgressCounts { Elements = 4200, PlacedTriangles = 4_812_330, Reused = 2210, Current = "Electrical Fixtures | Duplex Receptacle | id 123456 | linked" });
    Shot(progress, theme + "-07-progress");
    progress.Complete();
    dialog.Dispose();
}
revit.Close();
