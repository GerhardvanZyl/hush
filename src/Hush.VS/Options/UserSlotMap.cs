using System;
using System.Collections.Generic;
using System.Linq;

namespace Hush.VS.Options;

/// <summary>
/// Stable mapping between user-defined category keys and the fixed pool of
/// "hush.user1..N" classification types. Persisted as a compact "key1:slot1;key2:slot2"
/// string so the same category keeps the same slot (and therefore the same F&amp;C entry)
/// across sessions.
/// </summary>
internal sealed class UserSlotMap
{
    private readonly Dictionary<string, int> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _slotCount;

    public UserSlotMap(int slotCount = Constants.UserSlotCount)
    {
        _slotCount = slotCount;
    }

    public int? Get(string categoryKey) =>
        _map.TryGetValue(categoryKey, out var s) ? s : (int?)null;

    public int Assign(string categoryKey)
    {
        if (_map.TryGetValue(categoryKey, out var existing)) return existing;
        for (int i = 1; i <= _slotCount; i++)
        {
            if (!_map.Values.Contains(i))
            {
                _map[categoryKey] = i;
                return i;
            }
        }
        // No slots free: alias to slot 1 so the user still sees *something* muted
        // rather than silently dropping the category.
        _map[categoryKey] = 1;
        return 1;
    }

    public string? CategoryForSlot(int slot)
    {
        foreach (var kv in _map)
            if (kv.Value == slot) return kv.Key;
        return null;
    }

    public string Serialize() =>
        string.Join(";", _map.Select(kv => $"{kv.Key}:{kv.Value}"));

    public static UserSlotMap Deserialize(string? raw, int slotCount = Constants.UserSlotCount)
    {
        var m = new UserSlotMap(slotCount);
        if (string.IsNullOrWhiteSpace(raw)) return m;
        foreach (var part in raw!.Split(';'))
        {
            var bits = part.Split(':');
            if (bits.Length == 2 && int.TryParse(bits[1], out var slot))
            {
                m._map[bits[0]] = slot;
            }
        }
        return m;
    }
}
