using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace Hush.VS.Classification;

internal static class MuteColors
{
    public static Color Parse(string? hex, byte fallback = 0x88)
    {
        if (string.IsNullOrEmpty(hex)) return Color.FromRgb(fallback, fallback, fallback);
        var s = hex!.TrimStart('#');
        if (s.Length == 6 &&
            byte.TryParse(s.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(s.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(s.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return Color.FromRgb(r, g, b);
        }
        return Color.FromRgb(fallback, fallback, fallback);
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = Constants.ClassTelemetry)]
[Name(Constants.ClassTelemetry)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class HushTelemetryFormat : ClassificationFormatDefinition
{
    public HushTelemetryFormat()
    {
        DisplayName = "Hush Telemetry";
        ForegroundColor = MuteColors.Parse("#7A7A7A");
        ForegroundOpacity = 0.55;
        IsItalic = true;
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = Constants.ClassLogging)]
[Name(Constants.ClassLogging)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class HushLoggingFormat : ClassificationFormatDefinition
{
    public HushLoggingFormat()
    {
        DisplayName = "Hush Logging";
        ForegroundColor = MuteColors.Parse("#888888");
        ForegroundOpacity = 0.60;
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = Constants.ClassSignature)]
[Name(Constants.ClassSignature)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class HushSignatureFormat : ClassificationFormatDefinition
{
    public HushSignatureFormat()
    {
        DisplayName = "Hush Signature";
        ForegroundOpacity = 0.80;
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = Constants.ClassGuards)]
[Name(Constants.ClassGuards)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class HushGuardsFormat : ClassificationFormatDefinition
{
    public HushGuardsFormat()
    {
        DisplayName = "Hush Guards";
        ForegroundColor = MuteColors.Parse("#C2C9BE");
        ForegroundOpacity = 0.85;
        IsItalic = true;
    }
}

internal abstract class HushUserSlotFormatBase : ClassificationFormatDefinition
{
    protected HushUserSlotFormatBase(int slot)
    {
        DisplayName = $"Hush User Slot {slot}";
        ForegroundColor = MuteColors.Parse("#8A8A8A");
        ForegroundOpacity = 0.60;
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = "hush.user1")]
[Name("hush.user1")]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class HushUserSlot1Format : HushUserSlotFormatBase { public HushUserSlot1Format() : base(1) { } }

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = "hush.user2")]
[Name("hush.user2")]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class HushUserSlot2Format : HushUserSlotFormatBase { public HushUserSlot2Format() : base(2) { } }

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = "hush.user3")]
[Name("hush.user3")]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class HushUserSlot3Format : HushUserSlotFormatBase { public HushUserSlot3Format() : base(3) { } }

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = "hush.user4")]
[Name("hush.user4")]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class HushUserSlot4Format : HushUserSlotFormatBase { public HushUserSlot4Format() : base(4) { } }

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = "hush.user5")]
[Name("hush.user5")]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class HushUserSlot5Format : HushUserSlotFormatBase { public HushUserSlot5Format() : base(5) { } }

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = "hush.user6")]
[Name("hush.user6")]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class HushUserSlot6Format : HushUserSlotFormatBase { public HushUserSlot6Format() : base(6) { } }

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = "hush.user7")]
[Name("hush.user7")]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class HushUserSlot7Format : HushUserSlotFormatBase { public HushUserSlot7Format() : base(7) { } }

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = "hush.user8")]
[Name("hush.user8")]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class HushUserSlot8Format : HushUserSlotFormatBase { public HushUserSlot8Format() : base(8) { } }
