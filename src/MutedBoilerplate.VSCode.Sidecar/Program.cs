using System.Text.Json;
using StreamJsonRpc;
using MutedBoilerplate.VSCode.Sidecar.Host;

namespace MutedBoilerplate.VSCode.Sidecar;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Redirect Console.Out to stderr so any stray write doesn't corrupt the JSON-RPC stream.
        var stdoutForRpc = Console.OpenStandardOutput();
        var stdinForRpc = Console.OpenStandardInput();
        Console.SetOut(Console.Error);

        try
        {
            var host = new SidecarHost();
            var server = new RpcServer(host);

            var formatter = new SystemTextJsonFormatter
            {
                JsonSerializerOptions =
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true,
                },
            };

            var handler = new HeaderDelimitedMessageHandler(stdoutForRpc, stdinForRpc, formatter);
            using var rpc = new JsonRpc(handler);
            rpc.AddLocalRpcTarget(server, new JsonRpcTargetOptions
            {
                AllowNonPublicInvocation = false,
                UseSingleObjectParameterDeserialization = true,
            });

            rpc.StartListening();
            await rpc.Completion.ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Sidecar fatal: " + ex);
            return 1;
        }
    }
}
