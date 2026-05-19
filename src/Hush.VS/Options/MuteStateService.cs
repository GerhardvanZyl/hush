using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using Hush.Core.Matching;
using Hush.Core.Model;
using Hush.Core.Rules;

namespace Hush.VS.Options;

/// <summary>
/// Process-wide singleton holding the active <see cref="RuleSet"/>, the per-category
/// toggle <see cref="MuteState"/>, and the shared <see cref="IMuteSpanProvider"/>.
/// Classifier and outlining tagger import this and listen for <see cref="Changed"/>.
/// </summary>
[Export(typeof(MuteStateService))]
internal sealed class MuteStateService
{
    private readonly object _gate = new();
    private RuleSet _ruleSet;
    private MuteState _state;
    private UserSlotMap _slots;
    private int _stateVersion;
    private int _ruleSetVersion;
    // Phase 8: avoid `$"...{CategoryKey}..."` allocation per outlining tag.
    private Dictionary<string, string> _collapsedForms = new(StringComparer.OrdinalIgnoreCase);

    public MuteStateService()
    {
        _ruleSet = RuleSet.LoadDefaults();
        _ruleSet.NormalizeCategories();
        _state = BuildInitialState(_ruleSet);
        _slots = new UserSlotMap();
        AssignSlotsForCategories();
        Provider = MuteSpanProvider.CreateDefault();
        CompiledPatterns.Warmup(_ruleSet);
        BuildCollapsedForms(_ruleSet);
    }

    public IMuteSpanProvider Provider { get; }

    public event EventHandler<MuteStateChangedEventArgs>? Changed;

    private void RaiseChanged(string? categoryKey)
    {
        Changed?.Invoke(this, categoryKey is null
            ? MuteStateChangedEventArgs.All
            : new MuteStateChangedEventArgs(categoryKey));
    }

    public RuleSet RuleSet { get { lock (_gate) return _ruleSet; } }
    public MuteState State { get { lock (_gate) return _state; } }
    public UserSlotMap Slots { get { lock (_gate) return _slots; } }

    // Monotonic version counters. SnapshotMuteCache keys its memoized results on
    // (snapshot, stateVersion, ruleSetVersion) — any bump here invalidates caches
    // without the cache needing to subscribe to Changed events.
    public int StateVersion => Volatile.Read(ref _stateVersion);
    public int RuleSetVersion => Volatile.Read(ref _ruleSetVersion);

    // Phase 6: atomically capture an immutable view of the state for
    // off-UI-thread compute. MuteState._enabled is a mutable Dictionary, so
    // background walks would race with Toggle otherwise. MuteState.Snapshot()
    // copies the dict under our gate; the returned MuteState owns its own copy.
    public MuteStateSnapshot Capture()
    {
        lock (_gate)
        {
            var copy = new MuteState(_state.Snapshot(), _state.ExclusionsEnabled);
            return new MuteStateSnapshot(copy, _ruleSet, _stateVersion, _ruleSetVersion);
        }
    }

    public void Toggle(string categoryKey)
    {
        lock (_gate) _state.Toggle(categoryKey);
        Interlocked.Increment(ref _stateVersion);
        RaiseChanged(categoryKey);
    }

    public void ToggleAll()
    {
        lock (_gate) _state.ToggleAll();
        Interlocked.Increment(ref _stateVersion);
        RaiseChanged(null);
    }

    public void ToggleExclusions()
    {
        lock (_gate) _state.ToggleExclusions();
        Interlocked.Increment(ref _stateVersion);
        RaiseChanged(null);
    }

    public void ToggleUserSlot(int slot)
    {
        var key = _slots.CategoryForSlot(slot);
        if (key is null) return;
        Toggle(key);
    }

    public void ReloadFromOptions(MuteOptionsPage options)
    {
        var newRules = LoadRulesOrDefault(options.RulesJsonPath);
        lock (_gate)
        {
            _ruleSet = newRules;
            _state = BuildInitialState(_ruleSet);
            _slots = UserSlotMap.Deserialize(options.UserSlotMapping);
            AssignSlotsForCategories();
            options.UserSlotMapping = _slots.Serialize();
        }
        Interlocked.Increment(ref _ruleSetVersion);
        Interlocked.Increment(ref _stateVersion);
        CompiledPatterns.Warmup(newRules);
        BuildCollapsedForms(newRules);
        RaiseChanged(null);
    }

    public string GetCollapsedForm(string categoryKey)
    {
        var forms = _collapsedForms;
        return forms.TryGetValue(categoryKey, out var s) ? s : "...";
    }

    private void BuildCollapsedForms(RuleSet rs)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cat in rs.AllCategories())
        {
            dict[cat.Key] = "..." + cat.Key + "...";
        }
        _collapsedForms = dict;
    }

    private void AssignSlotsForCategories()
    {
        foreach (var cat in _ruleSet.AllCategories())
        {
            if (cat.IsBuiltIn) continue;
            _slots.Assign(cat.Key);
        }
    }

    private static RuleSet LoadRulesOrDefault(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try { return RuleSet.LoadFromFile(path!); }
            catch { /* fall through to defaults */ }
        }
        return RuleSet.LoadDefaults();
    }

    private static MuteState BuildInitialState(RuleSet rs)
    {
        var keys = new System.Collections.Generic.List<string>();
        foreach (var c in rs.AllCategories()) keys.Add(c.Key);
        return MuteState.AllOn(keys);
    }
}
