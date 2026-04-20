using System.Text.Json.Serialization;

namespace MutedBoilerplate.Core.Rules;

public sealed class ExclusionRule
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = RuleKind.Identifier;

    [JsonPropertyName("pattern")]
    public RulePattern Pattern { get; set; } = new();

    [JsonPropertyName("appliesTo")]
    public string AppliesTo { get; set; } = "*";
}
