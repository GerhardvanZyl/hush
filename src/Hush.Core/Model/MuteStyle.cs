namespace Hush.Core.Model;

public sealed class MuteStyle
{
    public string? Foreground { get; set; }
    public string? Background { get; set; }
    public double Opacity { get; set; } = 0.6;
    public int FontSizePercent { get; set; } = 100;
    public string? Typeface { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool AutoCollapse { get; set; }

    public static MuteStyle DefaultFor(string categoryKey) => categoryKey switch
    {
        MuteCategory.TelemetryKey => new MuteStyle
        {
            Foreground = "#7A7A7A",
            Opacity = 0.55,
            FontSizePercent = 90,
            Italic = true,
        },
        MuteCategory.LoggingKey => new MuteStyle
        {
            Foreground = "#888888",
            Opacity = 0.60,
            FontSizePercent = 90,
        },
        MuteCategory.SignatureKey => new MuteStyle
        {
            Opacity = 0.80,
            FontSizePercent = 92,
        },
        MuteCategory.GuardsKey => new MuteStyle
        {
            Foreground = "#7F8A7A",
            Opacity = 0.55,
            FontSizePercent = 90,
            Italic = true,
        },
        _ => new MuteStyle
        {
            Foreground = "#8A8A8A",
            Opacity = 0.60,
            FontSizePercent = 100,
        },
    };
}
