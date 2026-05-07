using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace MutedBoilerplate.VS.Classification;

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
internal sealed class MutedTelemetryFormat : ClassificationFormatDefinition
{
    public MutedTelemetryFormat()
    {
        DisplayName = "Muted Telemetry";
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
internal sealed class MutedLoggingFormat : ClassificationFormatDefinition
{
    public MutedLoggingFormat()
    {
        DisplayName = "Muted Logging";
        ForegroundColor = MuteColors.Parse("#888888");
        ForegroundOpacity = 0.60;
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = Constants.ClassSignature)]
[Name(Constants.ClassSignature)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class MutedSignatureFormat : ClassificationFormatDefinition
{
    public MutedSignatureFormat()
    {
        DisplayName = "Muted Signature";
        ForegroundOpacity = 0.80;
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = Constants.ClassGuards)]
[Name(Constants.ClassGuards)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class MutedGuardsFormat : ClassificationFormatDefinition
{
    public MutedGuardsFormat()
    {
        DisplayName = "Muted Guards";
        ForegroundColor = MuteColors.Parse("#9FA79B");
        ForegroundOpacity = 0.55;
        IsItalic = true;
    }
}

internal abstract class MutedUserSlotFormatBase : ClassificationFormatDefinition
{
    protected MutedUserSlotFormatBase(int slot)
    {
        DisplayName = $"Muted User Slot {slot}";
        ForegroundColor = MuteColors.Parse("#8A8A8A");
        ForegroundOpacity = 0.60;
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = "muted.user1")]
[Name("muted.user1")]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class MutedUserSlot1Format : MutedUserSlotFormatBase { public MutedUserSlot1Format() : base(1) { } }

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = "muted.user2")]
[Name("muted.user2")]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class MutedUserSlot2Format : MutedUserSlotFormatBase { public MutedUserSlot2Format() : base(2) { } }

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = "muted.user3")]
[Name("muted.user3")]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class MutedUserSlot3Format : MutedUserSlotFormatBase { public MutedUserSlot3Format() : base(3) { } }

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = "muted.user4")]
[Name("muted.user4")]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class MutedUserSlot4Format : MutedUserSlotFormatBase { public MutedUserSlot4Format() : base(4) { } }

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = "muted.user5")]
[Name("muted.user5")]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class MutedUserSlot5Format : MutedUserSlotFormatBase { public MutedUserSlot5Format() : base(5) { } }

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = "muted.user6")]
[Name("muted.user6")]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class MutedUserSlot6Format : MutedUserSlotFormatBase { public MutedUserSlot6Format() : base(6) { } }

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = "muted.user7")]
[Name("muted.user7")]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class MutedUserSlot7Format : MutedUserSlotFormatBase { public MutedUserSlot7Format() : base(7) { } }

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = "muted.user8")]
[Name("muted.user8")]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class MutedUserSlot8Format : MutedUserSlotFormatBase { public MutedUserSlot8Format() : base(8) { } }
