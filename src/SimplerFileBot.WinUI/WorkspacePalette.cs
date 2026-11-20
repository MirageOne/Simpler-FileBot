using Microsoft.UI.Xaml.Media;

namespace SimplerFileBot.WinUI;

internal sealed class WorkspacePalette
{
    public SolidColorBrush Canvas { get; } = new();
    public SolidColorBrush Rail { get; } = new();
    public SolidColorBrush Surface { get; } = new();
    public SolidColorBrush SurfaceAlt { get; } = new();
    public SolidColorBrush Border { get; } = new();
    public SolidColorBrush Text { get; } = new();
    public SolidColorBrush Muted { get; } = new();
    public SolidColorBrush Accent { get; } = new();
    public SolidColorBrush AccentText { get; } = new();
    public SolidColorBrush Selection { get; } = new();
    public SolidColorBrush InputSurface { get; } = new();
    public SolidColorBrush InputLine { get; } = new();
    public SolidColorBrush Error { get; } = new();
    public SolidColorBrush ErrorSurface { get; } = new();
    public SolidColorBrush Success { get; } = new();
    public SolidColorBrush Warning { get; } = new();
    public SolidColorBrush WarningSurface { get; } = new();

    public void Apply(bool dark)
    {
        Canvas.Color = Color(dark ? "1F232A" : "F5F7FB");
        Rail.Color = Color(dark ? "242830" : "FAFBFC");
        Surface.Color = Color(dark ? "272B33" : "FFFFFF");
        SurfaceAlt.Color = Color(dark ? "242830" : "FAFBFC");
        Border.Color = Color(dark ? "454C58" : "DCE0E6");
        Text.Color = Color(dark ? "F4F5F7" : "1B1B1B");
        Muted.Color = Color(dark ? "AAB3C2" : "657080");
        Accent.Color = Color("0067C0");
        AccentText.Color = Color("FFFFFF");
        Selection.Color = Color(dark ? "17496F" : "E7F1FC");
        InputSurface.Color = Color(dark ? "19334C" : "EFF7FF");
        InputLine.Color = Color(dark ? "62AEF5" : "1768A7");
        Error.Color = Color(dark ? "FF929F" : "A42636");
        ErrorSurface.Color = Color(dark ? "3A292D" : "FFF0F1");
        Success.Color = Color(dark ? "78D99D" : "16803C");
        Warning.Color = Color(dark ? "F2C365" : "A86600");
        WarningSurface.Color = Color(dark ? "3B382F" : "FFF7E6");
    }

    private static Windows.UI.Color Color(string hex) => new()
    {
        A = 255,
        R = Convert.ToByte(hex[..2], 16),
        G = Convert.ToByte(hex[2..4], 16),
        B = Convert.ToByte(hex[4..6], 16)
    };
}
