namespace MutedBoilerplate.Core.Rules;

public static class RuleKind
{
    public const string RoslynCall = "roslynCall";
    public const string Signature = "signature";
    public const string Regex = "regex";
    public const string Identifier = "identifier";
    public const string Guard = "guard";

    // Implemented in the VS Code extension via the TypeScript Compiler API.
    // Core itself doesn't dispatch this kind — Roslyn isn't usable for TS/JS.
    public const string TsCall = "tsCall";
}
