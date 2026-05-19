using System;
using System.Collections.Generic;
using System.Linq;

namespace Hush.Core.Model;

public sealed class MuteState
{
    private readonly Dictionary<string, bool> _enabled;
    private bool _exclusionsEnabled;

    public MuteState(IEnumerable<KeyValuePair<string, bool>> initial, bool exclusionsEnabled = true)
    {
        _enabled = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in initial) _enabled[kv.Key] = kv.Value;
        _exclusionsEnabled = exclusionsEnabled;
    }

    public static MuteState AllOn(IEnumerable<string> categoryKeys, bool exclusionsEnabled = true) =>
        new(categoryKeys.Select(k => new KeyValuePair<string, bool>(k, true)), exclusionsEnabled);

    public bool ExclusionsEnabled => _exclusionsEnabled;

    public bool IsEnabled(string categoryKey) =>
        _enabled.TryGetValue(categoryKey, out var v) && v;

    public IReadOnlyDictionary<string, bool> Snapshot() =>
        new Dictionary<string, bool>(_enabled, StringComparer.OrdinalIgnoreCase);

    public event EventHandler? Changed;

    public void Set(string categoryKey, bool enabled)
    {
        _enabled.TryGetValue(categoryKey, out var prev);
        if (prev == enabled && _enabled.ContainsKey(categoryKey)) return;
        _enabled[categoryKey] = enabled;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Toggle(string categoryKey)
    {
        var next = !IsEnabled(categoryKey);
        _enabled[categoryKey] = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleAll()
    {
        var keys = _enabled.Keys.ToList();
        var anyOn = keys.Any(k => _enabled[k]);
        var next = !anyOn;
        foreach (var k in keys) _enabled[k] = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetExclusionsEnabled(bool enabled)
    {
        if (_exclusionsEnabled == enabled) return;
        _exclusionsEnabled = enabled;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleExclusions()
    {
        _exclusionsEnabled = !_exclusionsEnabled;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void EnsureCategory(string categoryKey)
    {
        if (!_enabled.ContainsKey(categoryKey))
        {
            _enabled[categoryKey] = true;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
