using StreamJsonRpc;
using Hush.VSCode.Sidecar.Protocol;

namespace Hush.VSCode.Sidecar.Host;

/// <summary>
/// Thin JSON-RPC adapter over <see cref="SidecarHost"/>. Method names use camelCase to match
/// the TypeScript client. StreamJsonRpc dispatches one request at a time per connection
/// unless <c>SynchronizationContext</c> is set, so we don't need extra serialization.
/// </summary>
internal sealed class RpcServer
{
    private readonly SidecarHost _host;

    public RpcServer(SidecarHost host) => _host = host;

    [JsonRpcMethod("initialize")]
    public InitializeResponse Initialize(InitializeRequest request) => _host.Initialize(request);

    [JsonRpcMethod("didOpen")]
    public void DidOpen(DidOpenRequest request) => _host.DidOpen(request);

    [JsonRpcMethod("didChange")]
    public bool DidChange(DidChangeRequest request) => _host.DidChange(request);

    [JsonRpcMethod("didClose")]
    public void DidClose(DidCloseRequest request) => _host.DidClose(request);

    [JsonRpcMethod("getSpans")]
    public GetSpansResponse GetSpans(GetSpansRequest request) => _host.GetSpans(request);

    [JsonRpcMethod("setMuteState")]
    public StateChangeResponse SetMuteState(SetMuteStateRequest request) => _host.SetMuteState(request);

    [JsonRpcMethod("setExclusionsEnabled")]
    public StateChangeResponse SetExclusionsEnabled(SetExclusionsEnabledRequest request) =>
        _host.SetExclusionsEnabled(request);

    [JsonRpcMethod("toggleAll")]
    public StateChangeResponse ToggleAll() => _host.ToggleAll();

    [JsonRpcMethod("reloadRules")]
    public ReloadRulesResponse ReloadRules(ReloadRulesRequest request) => _host.ReloadRules(request);

    [JsonRpcMethod("shutdown")]
    public void Shutdown()
    {
        // Client signals graceful exit. Returning from this method lets the dispatcher
        // continue; Program.cs awaits the connection completion task which resolves
        // when the client closes its stdout pipe.
    }
}
