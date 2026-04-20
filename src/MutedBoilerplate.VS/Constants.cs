using System;

namespace MutedBoilerplate.VS;

internal static class Constants
{
    public const string PackageGuidString = "7b4b1d6c-1f0a-4d4f-9e1d-1a6b5b0a2e10";
    public const string CommandSetGuidString = "7b4b1d6c-1f0a-4d4f-9e1d-1a6b5b0a2e11";

    public static readonly Guid PackageGuid = new(PackageGuidString);
    public static readonly Guid CommandSetGuid = new(CommandSetGuidString);

    // Command IDs (must match the VSCT file).
    public const int CmdToggleTelemetry = 0x0100;
    public const int CmdToggleLogging   = 0x0101;
    public const int CmdToggleSignature = 0x0102;
    public const int CmdToggleAll       = 0x0103;
    public const int CmdToggleExclusions = 0x0104;
    public const int CmdToggleUser1     = 0x0110;
    public const int CmdToggleUser2     = 0x0111;
    public const int CmdToggleUser3     = 0x0112;
    public const int CmdToggleUser4     = 0x0113;
    public const int CmdToggleUser5     = 0x0114;
    public const int CmdToggleUser6     = 0x0115;
    public const int CmdToggleUser7     = 0x0116;
    public const int CmdToggleUser8     = 0x0117;

    public const int UserSlotCount = 8;

    public const string ContentTypeCSharp = "CSharp";
    public const string ContentTypeText = "text";

    // Classification type names — must be stable; the F&C UI keys off these.
    public const string ClassificationPrefix = "muted.";
    public const string ClassTelemetry = "muted.telemetry";
    public const string ClassLogging = "muted.logging";
    public const string ClassSignature = "muted.signature";
    public static string UserSlotClass(int slot) => $"muted.user{slot}";
}
