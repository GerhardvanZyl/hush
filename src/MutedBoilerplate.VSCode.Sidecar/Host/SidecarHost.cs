using Microsoft.CodeAnalysis.Text;
using MutedBoilerplate.Core.Matching;
using MutedBoilerplate.Core.Model;
using MutedBoilerplate.Core.Rules;
using MutedBoilerplate.VSCode.Sidecar.Protocol;

namespace MutedBoilerplate.VSCode.Sidecar.Host;

/// <summary>
/// Owns <see cref="MuteSpanProvider"/>, <see cref="MuteState"/>, current <see cref="RuleSet"/>,
/// <see cref="DocumentStore"/>, and <see cref="SpanCache"/>. Single instance per sidecar process.
/// Mirrors <c>MuteStateService</c> from the VS extension.
/// </summary>
internal sealed class SidecarHost
{
    private readonly object _gate = new();
    private readonly MuteSpanProvider _provider = MuteSpanProvider.CreateDefault();
    private readonly DocumentStore _docs = new();
    private readonly SpanCache _cache = new();

    private RuleSet _ruleSet;
    private MuteState _state;
    private long _stateVersion;
    private long _ruleSetVersion;

    public SidecarHost()
    {
        _ruleSet = RuleSet.LoadDefaults();
        _state = MuteState.AllOn(_ruleSet.AllCategories().Select(c => c.Key));
    }

    public InitializeResponse Initialize(InitializeRequest request)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(request.RulesPath))
            {
                _ruleSet = RuleSet.LoadFromFile(request.RulesPath);
            }

            var categoryKeys = _ruleSet.AllCategories().Select(c => c.Key).ToList();
            _state = new MuteState(BuildInitialState(categoryKeys, request.InitialState),
                request.ExclusionsEnabled ?? true);

            _stateVersion++;
            _ruleSetVersion++;
            _cache.Clear();

            return new InitializeResponse
            {
                Categories = BuildCategoryDtos(),
                StateVersion = _stateVersion,
                RuleSetVersion = _ruleSetVersion,
                ExclusionsEnabled = _state.ExclusionsEnabled,
            };
        }
    }

    public void DidOpen(DidOpenRequest req)
    {
        _docs.DidOpen(req.Uri, req.LanguageId, req.Version, req.Content);
        _cache.Invalidate(req.Uri);
    }

    public bool DidChange(DidChangeRequest req)
    {
        var ranges = new TextChangeRange[req.Changes.Length];
        var texts = new string[req.Changes.Length];
        for (int i = 0; i < req.Changes.Length; i++)
        {
            var c = req.Changes[i];
            ranges[i] = new TextChangeRange(new TextSpan(c.Start, c.Length), c.Text.Length);
            texts[i] = c.Text;
        }
        _cache.Invalidate(req.Uri);
        return _docs.DidChange(req.Uri, req.Version, ranges, texts);
    }

    public void DidClose(DidCloseRequest req)
    {
        _docs.DidClose(req.Uri);
        _cache.Invalidate(req.Uri);
    }

    public GetSpansResponse GetSpans(GetSpansRequest req)
    {
        var doc = _docs.Get(req.Uri);
        if (doc is null)
        {
            return new GetSpansResponse
            {
                Uri = req.Uri,
                Version = req.Version ?? -1,
                StateVersion = _stateVersion,
                RuleSetVersion = _ruleSetVersion,
                Spans = System.Array.Empty<MuteSpanDto>(),
            };
        }

        long stateVer, ruleVer;
        MuteState state;
        RuleSet ruleSet;
        lock (_gate)
        {
            stateVer = _stateVersion;
            ruleVer = _ruleSetVersion;
            state = _state;
            ruleSet = _ruleSet;
        }

        if (_cache.TryGet(req.Uri, doc.Version, stateVer, ruleVer, out var cached))
        {
            return BuildResponse(req.Uri, doc.Version, stateVer, ruleVer, cached);
        }

        var ctx = new MatchContext(doc.Text, doc.Tree);
        var spans = _provider.GetSpans(ctx, state, ruleSet).ToArray();
        _cache.Put(req.Uri, doc.Version, stateVer, ruleVer, spans);
        return BuildResponse(req.Uri, doc.Version, stateVer, ruleVer, spans);
    }

    public StateChangeResponse SetMuteState(SetMuteStateRequest req)
    {
        lock (_gate)
        {
            _state.Set(req.CategoryKey, req.Enabled);
            _stateVersion++;
            return BuildStateChange();
        }
    }

    public StateChangeResponse SetExclusionsEnabled(SetExclusionsEnabledRequest req)
    {
        lock (_gate)
        {
            _state.SetExclusionsEnabled(req.Enabled);
            _stateVersion++;
            return BuildStateChange();
        }
    }

    public StateChangeResponse ToggleAll()
    {
        lock (_gate)
        {
            _state.ToggleAll();
            _stateVersion++;
            return BuildStateChange();
        }
    }

    public ReloadRulesResponse ReloadRules(ReloadRulesRequest req)
    {
        lock (_gate)
        {
            _ruleSet = string.IsNullOrWhiteSpace(req.Path)
                ? RuleSet.LoadDefaults()
                : RuleSet.LoadFromFile(req.Path);

            var prevSnap = _state.Snapshot();
            var keys = _ruleSet.AllCategories().Select(c => c.Key).ToList();
            var initial = keys.Select(k => new KeyValuePair<string, bool>(k,
                prevSnap.TryGetValue(k, out var v) ? v : true));
            _state = new MuteState(initial, _state.ExclusionsEnabled);

            _ruleSetVersion++;
            _cache.Clear();

            return new ReloadRulesResponse
            {
                RuleSetVersion = _ruleSetVersion,
                Categories = BuildCategoryDtos(),
            };
        }
    }

    public IEnumerable<DocumentStore.Entry> OpenDocuments() => _docs.All();

    private GetSpansResponse BuildResponse(string uri, int version, long stateVer, long ruleVer, MuteSpan[] spans)
    {
        var dtos = new MuteSpanDto[spans.Length];
        for (int i = 0; i < spans.Length; i++)
        {
            var s = spans[i];
            dtos[i] = new MuteSpanDto
            {
                Start = s.Span.Start,
                End = s.Span.End,
                CategoryKey = s.CategoryKey,
                RuleName = s.RuleName,
                Scope = s.Scope.ToString(),
            };
        }
        return new GetSpansResponse
        {
            Uri = uri,
            Version = version,
            StateVersion = stateVer,
            RuleSetVersion = ruleVer,
            Spans = dtos,
        };
    }

    private StateChangeResponse BuildStateChange()
    {
        var snap = _state.Snapshot();
        var arr = new CategoryStateDto[snap.Count];
        int i = 0;
        foreach (var kv in snap)
            arr[i++] = new CategoryStateDto { Key = kv.Key, Enabled = kv.Value };
        return new StateChangeResponse
        {
            StateVersion = _stateVersion,
            ExclusionsEnabled = _state.ExclusionsEnabled,
            Categories = arr,
        };
    }

    private CategoryDto[] BuildCategoryDtos()
    {
        var snap = _state.Snapshot();
        return _ruleSet.AllCategories()
            .Select(c => new CategoryDto
            {
                Key = c.Key,
                DisplayName = c.DisplayName,
                IsBuiltIn = c.IsBuiltIn,
                Enabled = snap.TryGetValue(c.Key, out var on) && on,
                Style = _ruleSet.StyleFor(c.Key),
            })
            .ToArray();
    }

    private static IEnumerable<KeyValuePair<string, bool>> BuildInitialState(
        IEnumerable<string> categoryKeys,
        CategoryStateDto[]? overrides)
    {
        var map = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var k in categoryKeys) map[k] = true;
        if (overrides is not null)
        {
            foreach (var o in overrides)
            {
                if (!string.IsNullOrWhiteSpace(o.Key)) map[o.Key] = o.Enabled;
            }
        }
        return map;
    }
}
