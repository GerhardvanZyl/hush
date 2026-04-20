using System.Text.Json.Serialization;

namespace MutedBoilerplate.Core.Rules;

public sealed class RulePattern
{
    [JsonPropertyName("methodNameGlob")]
    public string? MethodNameGlob { get; set; }

    [JsonPropertyName("receiverTypeGlob")]
    public string? ReceiverTypeGlob { get; set; }

    [JsonPropertyName("regex")]
    public string? Regex { get; set; }

    [JsonPropertyName("identifier")]
    public string? Identifier { get; set; }

    [JsonPropertyName("attributesAny")]
    public string[]? AttributesAny { get; set; }

    [JsonPropertyName("modifiersAny")]
    public string[]? ModifiersAny { get; set; }
}
