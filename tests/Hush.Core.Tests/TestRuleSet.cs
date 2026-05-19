using Hush.Core.Model;
using Hush.Core.Rules;

namespace Hush.Core.Tests;

internal static class TestRuleSet
{
    public static MuteState AllOn(RuleSet rs)
    {
        var keys = new System.Collections.Generic.List<string>();
        foreach (var c in rs.AllCategories()) keys.Add(c.Key);
        return MuteState.AllOn(keys);
    }

    public static MuteRule LoggerCallRule(string name = "ilogger") => new()
    {
        Name = name,
        Category = MuteCategory.LoggingKey,
        Kind = RuleKind.RoslynCall,
        Pattern = new RulePattern { ReceiverTypeGlob = "ILogger*", MethodNameGlob = "Log*" },
        Scope = MuteScope.WholeStatement,
    };

    public static MuteRule TelemetryClientRule(string name = "telemetry") => new()
    {
        Name = name,
        Category = MuteCategory.TelemetryKey,
        Kind = RuleKind.RoslynCall,
        Pattern = new RulePattern { ReceiverTypeGlob = "TelemetryClient", MethodNameGlob = "Track*" },
        Scope = MuteScope.WholeStatement,
    };

    public static MuteRule SignatureRule() => new()
    {
        Name = "method-sig",
        Category = MuteCategory.SignatureKey,
        Kind = RuleKind.Signature,
        Pattern = new RulePattern(),
        Scope = MuteScope.SignatureRange,
    };
}
