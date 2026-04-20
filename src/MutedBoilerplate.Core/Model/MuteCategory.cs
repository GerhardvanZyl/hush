using System;

namespace MutedBoilerplate.Core.Model;

public sealed class MuteCategory : IEquatable<MuteCategory>
{
    public const string TelemetryKey = "telemetry";
    public const string LoggingKey = "logging";
    public const string SignatureKey = "signature";

    public static readonly MuteCategory Telemetry = new(TelemetryKey, "Telemetry", isBuiltIn: true);
    public static readonly MuteCategory Logging = new(LoggingKey, "Logging", isBuiltIn: true);
    public static readonly MuteCategory Signature = new(SignatureKey, "Signature", isBuiltIn: true);

    public static readonly MuteCategory[] BuiltIns = { Telemetry, Logging, Signature };

    public MuteCategory(string key, string displayName, bool isBuiltIn = false)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key required.", nameof(key));
        Key = key;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? key : displayName;
        IsBuiltIn = isBuiltIn;
    }

    public string Key { get; }
    public string DisplayName { get; }
    public bool IsBuiltIn { get; }

    public static bool IsBuiltInKey(string key) =>
        key == TelemetryKey || key == LoggingKey || key == SignatureKey;

    public bool Equals(MuteCategory? other) =>
        other is not null && string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as MuteCategory);

    public override int GetHashCode() =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(Key);

    public override string ToString() => Key;
}
