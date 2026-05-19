using System.Text.Json.Serialization;
using Hush.Core.Model;

namespace Hush.Core.Rules;

public sealed class MuteRule
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = RuleKind.RoslynCall;

    [JsonPropertyName("pattern")]
    public RulePattern Pattern { get; set; } = new();

    [JsonPropertyName("scope")]
    public MuteScope Scope { get; set; } = MuteScope.Match;

    [JsonPropertyName("excludes")]
    public ExclusionRule[]? Excludes { get; set; }
}
