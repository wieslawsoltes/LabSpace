using LabSpace.Controls;
using LabSpace.Editing;

namespace LabSpace.Workbench;

public sealed partial class InstrumentWorkbench
{
    private readonly LabPane _debugPane = new("Debug") { Visibility = Visibility.Collapsed, Height = 235 };
    private readonly DispatcherTimer _debugTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private bool _debugDirty = true;
    public DebugWindowControl DebugWindow { get; private set; } = null!;
    public bool DebugWindowVisible => _debugPane.Visibility == Visibility.Visible;
    private void InitializeDebugger(Grid root, StackPanel tools)
    {
        DebugWindow = new(Session); _debugPane.PaneContent = DebugWindow;
        DebugWindow.ProbeLocated += () => { SetView(StudioView.BlockDiagram); BlockDiagram.Fit(); };
        _debugPane.Commands.Children.Add(Command("debug-close", "×", () => ShowDebugWindow(false)));
        tools.Children.Insert(tools.Children.IndexOf(_commands["probe"]) + 1, Command("debug-window", "Debug", () => ShowDebugWindow(!DebugWindowVisible)));
        Grid.SetRow(_debugPane, 4); root.Children.Add(_debugPane);
        _debugTimer.Tick += (_, _) => { if (DebugWindowVisible && _debugDirty) { _debugDirty = false; DebugWindow.Refresh(); } };
        _debugTimer.Start();
    }
    public void ShowDebugWindow(bool visible)
    {
        _debugPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible)
        {
            _errorsHost.Visibility = Visibility.Collapsed;
            _debugPane.Height = Math.Clamp(ActualHeight * .31, 150, 280);
            _debugDirty = false; DebugWindow.Refresh();
        }
        _commands["debug-window"].SetActive(visible);
    }
    private void UpdateDebugger(SessionChange change)
    {
        if ((change & (SessionChange.Debug | SessionChange.Document | SessionChange.Navigation | SessionChange.Execution)) != 0) _debugDirty = true;
        _commands["step"].IsEnabled = _commands["step-into"].IsEnabled = Session.DebuggingEnabled;
        _commands["step-out"].IsEnabled = Session.DebuggingEnabled && Session.IsPaused;
    }
}
