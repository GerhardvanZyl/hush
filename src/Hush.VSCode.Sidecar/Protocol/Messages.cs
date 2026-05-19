using System.Text.Json.Serialization;
using Hush.Core.Model;

namespace Hush.VSCode.Sidecar.Protocol;

public sealed class InitializeRequest
{
    [JsonPropertyName("rulesPath")]
    public string? RulesPath { get; set; }

    [JsonPropertyName("workspaceFolders")]
    public string[]? WorkspaceFolders { get; set; }

    [JsonPropertyName("initialState")]
    public CategoryStateDto[]? InitialState { get; set; }

    [JsonPropertyName("exclusionsEnabled")]
    public bool? ExclusionsEnabled { get; set; }
}

public sealed class InitializeResponse
{
    [JsonPropertyName("categories")]
    public CategoryDto[] Categories { get; set; } = System.Array.Empty<CategoryDto>();

    [JsonPropertyName("stateVersion")]
    public long StateVersion { get; set; }

    [JsonPropertyName("ruleSetVersion")]
    public long RuleSetVersion { get; set; }

    [JsonPropertyName("exclusionsEnabled")]
    public bool ExclusionsEnabled { get; set; }

    [JsonPropertyName("tsCallRules")]
    public TsCallRuleDto[] TsCallRules { get; set; } = System.Array.Empty<TsCallRuleDto>();
}

/// <summary>
/// A rule of kind <c>tsCall</c> shipped to the VS Code extension so its in-process
/// TypeScript Compiler API matcher can apply it. Core itself doesn't dispatch this kind.
/// </summary>
public sealed class TsCallRuleDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("receiverGlob")]
    public string? ReceiverGlob { get; set; }

    [JsonPropertyName("methodNameGlob")]
    public string? MethodNameGlob { get; set; }

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "";
}

public sealed class CategoryDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("isBuiltIn")]
    public bool IsBuiltIn { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("style")]
    public MuteStyle Style { get; set; } = new();
}

public sealed class CategoryStateDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

public sealed class DidOpenRequest
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = "";

    [JsonPropertyName("languageId")]
    public string LanguageId { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}

public sealed class DidChangeRequest
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("changes")]
    public TextChangeDto[] Changes { get; set; } = System.Array.Empty<TextChangeDto>();
}

public sealed class TextChangeDto
{
    [JsonPropertyName("start")]
    public int Start { get; set; }

    [JsonPropertyName("length")]
    public int Length { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}

public sealed class DidCloseRequest
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = "";
}

public sealed class GetSpansRequest
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = "";

    [JsonPropertyName("version")]
    public int? Version { get; set; }
}

public sealed class GetSpansResponse
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("stateVersion")]
    public long StateVersion { get; set; }

    [JsonPropertyName("ruleSetVersion")]
    public long RuleSetVersion { get; set; }

    [JsonPropertyName("spans")]
    public MuteSpanDto[] Spans { get; set; } = System.Array.Empty<MuteSpanDto>();
}

public sealed class MuteSpanDto
{
    [JsonPropertyName("start")]
    public int Start { get; set; }

    [JsonPropertyName("end")]
    public int End { get; set; }

    [JsonPropertyName("categoryKey")]
    public string CategoryKey { get; set; } = "";

    [JsonPropertyName("ruleName")]
    public string RuleName { get; set; } = "";

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "";
}

public sealed class SetMuteStateRequest
{
    [JsonPropertyName("categoryKey")]
    public string CategoryKey { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

public sealed class SetExclusionsEnabledRequest
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

public sealed class StateChangeResponse
{
    [JsonPropertyName("stateVersion")]
    public long StateVersion { get; set; }

    [JsonPropertyName("exclusionsEnabled")]
    public bool ExclusionsEnabled { get; set; }

    [JsonPropertyName("categories")]
    public CategoryStateDto[] Categories { get; set; } = System.Array.Empty<CategoryStateDto>();
}

public sealed class ReloadRulesRequest
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }
}

public sealed class ReloadRulesResponse
{
    [JsonPropertyName("ruleSetVersion")]
    public long RuleSetVersion { get; set; }

    [JsonPropertyName("categories")]
    public CategoryDto[] Categories { get; set; } = System.Array.Empty<CategoryDto>();

    [JsonPropertyName("tsCallRules")]
    public TsCallRuleDto[] TsCallRules { get; set; } = System.Array.Empty<TsCallRuleDto>();
}
