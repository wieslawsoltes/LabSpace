using LabSpace.Controls;
using LabSpace.Documents;
using LabSpace.Editing;
using LabSpace.Skia;
using LabSpace.Storage;
using LabSpace.Workbench;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;

namespace LabSpace.App;

public partial class App : Application
{
    private Window? _window;
    private InstrumentWorkbench? _workbench;
    private readonly LabFonts _fonts = new();
#if __WASM__
    private BrowserDiagnostics? _diagnostics;
#endif
    public App() { InitializeComponent(); RequestedTheme = ApplicationTheme.Light; }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new Window { Title = "LabSpace — Graphical Instrumentation" };
        _window.Content = new Grid { Background = LabTheme.Brush("#EFEFEF"), Children = { new TextBlock { Text = "LabSpace\nLoading graphical instrumentation studio…", FontSize = 22, TextAlignment = TextAlignment.Center, Foreground = LabTheme.Brush("#535353"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } } }; _window.Activate();
        try
        {
#if __WASM__
            IProjectStorage storage = new BrowserProjectStorage();
#else
            IProjectStorage storage = new DesktopProjectStorage();
#endif
            var project = Examples.Create(); string? warning = null;
            try { var recovery = await storage.ReadRecoveryAsync(); if (!string.IsNullOrWhiteSpace(recovery)) project = ProjectSerializer.Load(recovery); }
            catch (Exception error) { warning = "Recovery could not be opened; the previous data has not been deleted: " + error.Message; }
            var font = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Fonts/Carlito-Regular.ttf"));
            using (var stream = await font.OpenStreamForReadAsync()) { using var buffer = new MemoryStream(); await stream.CopyToAsync(buffer); buffer.Position = 0; _fonts.Load(buffer); }
            LabTheme.Font = new FontFamily("ms-appx:///Assets/Fonts/Carlito-Regular.ttf#Carlito");
            var session = new InstrumentSession(project); _workbench = new(session, storage, _fonts); _window.Content = _workbench;
#if __WASM__
            if (BrowserFiles.IsTestMode()) _diagnostics = new(_workbench);
#endif
            _window.Closed += (_, _) =>
            {
#if __WASM__
                _diagnostics?.Dispose();
#endif
                _workbench.Dispose(); _fonts.Dispose();
            };
            if (warning is not null) session.Message(warning);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error); _workbench?.Dispose();
            _window.Content = new ScrollViewer { Content = new TextBlock { Text = "LabSpace could not start.\n\n" + error + "\n\nYour saved projects have not been deleted. Reload to retry.", TextWrapping = TextWrapping.Wrap, FontSize = 15, Margin = new Thickness(30) } };
        }
    }
}
