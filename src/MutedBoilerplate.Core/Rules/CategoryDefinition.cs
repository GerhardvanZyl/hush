using System.Collections.Generic;
using System.Text.Json.Serialization;
using MutedBoilerplate.Core.Model;

namespace MutedBoilerplate.Core.Rules;

public sealed class CategoryDefinition
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("style")]
    public MuteStyle? Style { get; set; }
}
