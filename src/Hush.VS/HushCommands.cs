using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Hush.Core.Model;
using Hush.VS.Options;

namespace Hush.VS;

internal static class HushCommands
{
    public static void Register(OleMenuCommandService svc, MuteStateService state)
    {
        Bind(svc, Constants.CmdToggleTelemetry, () => state.Toggle(MuteCategory.TelemetryKey));
        Bind(svc, Constants.CmdToggleLogging,   () => state.Toggle(MuteCategory.LoggingKey));
        Bind(svc, Constants.CmdToggleSignature, () => state.Toggle(MuteCategory.SignatureKey));
        Bind(svc, Constants.CmdToggleGuards,    () => state.Toggle(MuteCategory.GuardsKey));
        Bind(svc, Constants.CmdToggleAll,        state.ToggleAll);
        Bind(svc, Constants.CmdToggleExclusions, state.ToggleExclusions);
        for (int slot = 1; slot <= Constants.UserSlotCount; slot++)
        {
            var captured = slot;
            Bind(svc, Constants.CmdToggleUser1 + (slot - 1), () => state.ToggleUserSlot(captured));
        }
    }

    private static void Bind(OleMenuCommandService svc, int cmdId, Action handler)
    {
        var id = new CommandID(Constants.CommandSetGuid, cmdId);
        var cmd = new MenuCommand((_, _) => handler(), id);
        svc.AddCommand(cmd);
    }
}
