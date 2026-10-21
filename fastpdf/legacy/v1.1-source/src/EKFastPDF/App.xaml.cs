using System.Windows;

namespace EKFastPDF;

public partial class App : Application
{
    protected override void OnExit(ExitEventArgs e)
    {
        PdfEngine.Shutdown();
        base.OnExit(e);
    }
}
