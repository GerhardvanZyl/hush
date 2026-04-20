using System;

namespace MutedBoilerplate.VS.Options;

// Phase 7: carries which category toggled so the classifier and outlining
// tagger can invalidate only the affected span union instead of the whole
// buffer. CategoryKey == null means "everything may have changed" — used
// for ToggleAll, ToggleExclusions, and ReloadFromOptions.
internal sealed class MuteStateChangedEventArgs : EventArgs
{
    public static readonly MuteStateChangedEventArgs All = new(null);

    public MuteStateChangedEventArgs(string? categoryKey)
    {
        CategoryKey = categoryKey;
    }

    public string? CategoryKey { get; }
    public bool AffectsAllCategories => CategoryKey is null;
}
