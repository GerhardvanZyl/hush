using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace MutedBoilerplate.VS.Options;

[Guid("8a3f5cf2-5d38-4a3a-9b3e-1f2c0a4cc101")]
public sealed class MuteOptionsPage : DialogPage
{
    [Category("Categories")]
    [DisplayName("Telemetry enabled")]
    [Description("Whether telemetry boilerplate starts muted when a file opens.")]
    public bool TelemetryEnabled { get; set; } = true;

    [Category("Categories")]
    [DisplayName("Logging enabled")]
    public bool LoggingEnabled { get; set; } = true;

    [Category("Categories")]
    [DisplayName("Signature enabled")]
    public bool SignatureEnabled { get; set; } = true;

    [Category("Auto-collapse")]
    [DisplayName("Auto-collapse telemetry")]
    [Description("Render telemetry statements as a collapsed outlining region by default.")]
    public bool AutoCollapseTelemetry { get; set; }

    [Category("Auto-collapse")]
    [DisplayName("Auto-collapse logging")]
    public bool AutoCollapseLogging { get; set; }

    [Category("Auto-collapse")]
    [DisplayName("Auto-collapse signature")]
    public bool AutoCollapseSignature { get; set; }

    [Category("Rules")]
    [DisplayName("Rules JSON file")]
    [Description("Optional path to a custom rules JSON file. When empty, the bundled defaults are used.")]
    public string? RulesJsonPath { get; set; }

    // Internal — persisted but not shown in the UI.
    [Browsable(false)]
    public string? UserSlotMapping { get; set; }
}
