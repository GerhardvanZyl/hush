using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MutedBoilerplate.Core.Model;

namespace MutedBoilerplate.Core.Rules;

public sealed class RuleSet
{
    [JsonPropertyName("categories")]
    public List<CategoryDefinition> Categories { get; set; } = new();

    [JsonPropertyName("rules")]
    public List<MuteRule> Rules { get; set; } = new();

    [JsonPropertyName("exclusions")]
    public List<ExclusionRule> Exclusions { get; set; } = new();

    public static JsonSerializerOptions JsonOptions { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return o;
    }

    public static RuleSet LoadDefaults()
    {
        var asm = typeof(RuleSet).Assembly;
        var name = asm.GetManifestResourceNames()
            .First(n => n.EndsWith("DefaultRules.json", StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static RuleSet Parse(string json)
    {
        var rs = JsonSerializer.Deserialize<RuleSet>(json, JsonOptions) ?? new RuleSet();
        rs.NormalizeCategories();
        return rs;
    }

    public static RuleSet LoadFromFile(string path) => Parse(File.ReadAllText(path));

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public void SaveToFile(string path) => File.WriteAllText(path, ToJson());

    public IEnumerable<MuteCategory> AllCategories()
    {
        foreach (var b in MuteCategory.BuiltIns) yield return b;
        foreach (var c in Categories)
        {
            if (MuteCategory.IsBuiltInKey(c.Key)) continue;
            yield return new MuteCategory(c.Key, c.DisplayName ?? c.Key, isBuiltIn: false);
        }
    }

    public MuteStyle StyleFor(string categoryKey)
    {
        var defined = Categories.FirstOrDefault(c =>
            string.Equals(c.Key, categoryKey, StringComparison.OrdinalIgnoreCase));
        return defined?.Style ?? MuteStyle.DefaultFor(categoryKey);
    }

    /// <summary>
    /// Ensures that every <see cref="MuteRule.Category"/> referenced by a rule has a
    /// matching <see cref="CategoryDefinition"/> so user-defined categories surface in
    /// downstream consumers without the user having to repeat themselves.
    /// </summary>
    public void NormalizeCategories()
    {
        // Phase 8: intern in place so every MuteSpan emitted from these rules
        // shares one string reference per category/rule name rather than a
        // fresh copy per JSON deserialization.
        foreach (var cat in Categories)
        {
            cat.Key = RuleStringInterner.Intern(cat.Key);
        }
        foreach (var rule in Rules)
        {
            rule.Category = RuleStringInterner.Intern(rule.Category);
            rule.Name = RuleStringInterner.Intern(rule.Name);
            if (rule.Excludes != null)
            {
                foreach (var ex in rule.Excludes)
                {
                    ex.Name = RuleStringInterner.Intern(ex.Name);
                    ex.AppliesTo = RuleStringInterner.Intern(ex.AppliesTo);
                }
            }
        }
        foreach (var ex in Exclusions)
        {
            ex.Name = RuleStringInterner.Intern(ex.Name);
            ex.AppliesTo = RuleStringInterner.Intern(ex.AppliesTo);
        }

        var existing = new HashSet<string>(
            Categories.Select(c => c.Key),
            StringComparer.OrdinalIgnoreCase);

        foreach (var rule in Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Category)) continue;
            if (MuteCategory.IsBuiltInKey(rule.Category)) continue;
            if (existing.Add(rule.Category))
            {
                Categories.Add(new CategoryDefinition { Key = rule.Category });
            }
        }
    }
}
