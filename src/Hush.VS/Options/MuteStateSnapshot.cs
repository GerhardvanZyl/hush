using Hush.Core.Model;
using Hush.Core.Rules;

namespace Hush.VS.Options;

// Phase 6: immutable bundle of (state, rules, versions) captured atomically
// so background compute doesn't have to re-take MuteStateService's gate or
// race with mutation. The MuteState inside owns its own dictionary copy.
internal readonly struct MuteStateSnapshot
{
    public MuteStateSnapshot(MuteState state, RuleSet ruleSet, int stateVersion, int ruleSetVersion)
    {
        State = state;
        RuleSet = ruleSet;
        StateVersion = stateVersion;
        RuleSetVersion = ruleSetVersion;
    }

    public MuteState State { get; }
    public RuleSet RuleSet { get; }
    public int StateVersion { get; }
    public int RuleSetVersion { get; }
}
