using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Markup;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using LabSpace.Skia;

namespace LabSpace.Controls;

public static class LabTheme
{
    public static FontFamily Font { get; set; } = new("Arial");
    public static SolidColorBrush Brush(string hex) { var c = SKColor.Parse(hex); return new(Windows.UI.Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue)); }
    public static TextBlock Text(string text, double size = 12, string color = "#242424") => new() { Text = text, FontSize = size, FontFamily = Font, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    public static Border Separator(bool vertical = true) => new() { Width = vertical ? 1 : double.NaN, Height = vertical ? double.NaN : 1, Background = Brush("#ABABAB"), Margin = vertical ? new Thickness(5, 3, 5, 3) : new Thickness(0, 3, 0, 3) };
}

public sealed class VectorIcon : SKCanvasElement, IDisposable
{
    private readonly LabDrawing _drawing;
    public string Icon { get; set; }
    public bool Active { get; set; }
    public VectorIcon(string icon, LabFonts fonts) { Icon = icon; _drawing = new(fonts); Width = 20; Height = 20; IsHitTestVisible = false; }
    protected override void RenderOverride(SKCanvas canvas, Size area) => IconPainter.Draw(canvas, Icon, new(0, 0, (float)area.Width, (float)area.Height), _drawing, Active);
    public void Dispose() => _drawing.Dispose();
}

public class LabButton : Button
{
    private static readonly Lazy<ControlTemplate> ClassicTemplate = new(() => (ControlTemplate)XamlReader.Load("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Border x:Name="Root" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">
            <VisualStateManager.VisualStateGroups><VisualStateGroup x:Name="CommonStates">
              <VisualState x:Name="Normal"/><VisualState x:Name="PointerOver"><VisualState.Setters><Setter Target="Root.Background" Value="#D9E5F1"/><Setter Target="Root.BorderBrush" Value="#7696B5"/></VisualState.Setters></VisualState>
              <VisualState x:Name="Pressed"><VisualState.Setters><Setter Target="Root.Background" Value="#C0D4E7"/></VisualState.Setters></VisualState>
              <VisualState x:Name="Disabled"><VisualState.Setters><Setter Target="Root.Opacity" Value="0.4"/></VisualState.Setters></VisualState>
            </VisualStateGroup></VisualStateManager.VisualStateGroups>
            <ContentPresenter Content="{TemplateBinding Content}" ContentTemplate="{TemplateBinding ContentTemplate}" Padding="{TemplateBinding Padding}" HorizontalContentAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalContentAlignment="{TemplateBinding VerticalContentAlignment}"/>
          </Border>
        </ControlTemplate>
        """));
    public LabButton(string label, Action action, string? icon = null, LabFonts? fonts = null, bool flat = true)
    {
        Template = ClassicTemplate.Value; FontFamily = LabTheme.Font; FontSize = 12; CornerRadius = new(0); Padding = new Thickness(7, 3, 7, 3); MinHeight = 25; MinWidth = 26;
        Background = LabTheme.Brush(flat ? "#EFEFEF" : "#E1E1E1"); BorderBrush = LabTheme.Brush(flat ? "#EFEFEF" : "#949494"); BorderThickness = new(1); VerticalContentAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(this, label); ToolTipService.SetToolTip(this, label);
        if (icon is not null && fonts is not null) { var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 }; row.Children.Add(new VectorIcon(icon, fonts)); if (!string.IsNullOrEmpty(label) && label.Length < 30) row.Children.Add(LabTheme.Text(label)); Content = row; }
        else Content = LabTheme.Text(label);
        Click += (_, _) => action();
    }
    public void SetActive(bool active) { Background = LabTheme.Brush(active ? "#CDDEEB" : "#EFEFEF"); BorderBrush = LabTheme.Brush(active ? "#7D9CB6" : "#EFEFEF"); }
}

public sealed class LabTextBox : TextBox
{
    public LabTextBox(string text = "", string name = "Value")
    {
        Text = text; FontFamily = LabTheme.Font; FontSize = 13; MinHeight = 27; Padding = new Thickness(5, 3, 5, 3); CornerRadius = new(0); BorderThickness = new(1); BorderBrush = LabTheme.Brush("#8D8D8D"); Background = LabTheme.Brush("#FFFFFF"); AutomationProperties.SetName(this, name);
    }
}

public sealed class LabPane : Grid
{
    private readonly Border _body = new();
    public TextBlock Caption { get; }
    public StackPanel Commands { get; } = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    public UIElement? PaneContent { get => _body.Child; set => _body.Child = value; }
    public LabPane(string title)
    {
        RowDefinitions.Add(new() { Height = new GridLength(27) }); RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        Background = LabTheme.Brush("#EFEFEF"); var header = new Grid { Background = LabTheme.Brush("#DDDDDD"), BorderBrush = LabTheme.Brush("#A8A8A8"), BorderThickness = new Thickness(0, 0, 0, 1) };
        Caption = LabTheme.Text(title, 12); Caption.Margin = new(7, 0, 60, 0); header.Children.Add(Caption); header.Children.Add(Commands); Children.Add(header); Grid.SetRow(_body, 1); Children.Add(_body);
    }
}
