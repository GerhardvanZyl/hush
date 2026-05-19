using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Hush.VS.Options;
using Task = System.Threading.Tasks.Task;

namespace Hush.VS;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("Hush", "Hush — visually quiet the boilerplate so the meaningful code stays loud.", "0.1.0")]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideOptionPage(typeof(MuteOptionsPage), "Hush", "General", 0, 0, true)]
[ProvideAutoLoad(Microsoft.VisualStudio.VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
[Guid(Constants.PackageGuidString)]
public sealed class HushPackage : AsyncPackage
{
    protected override async Task InitializeAsync(CancellationToken ct, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(ct);

        var commandService = (OleMenuCommandService)await GetServiceAsync(typeof(IMenuCommandService));
        var componentModel = (IComponentModel)await GetServiceAsync(typeof(SComponentModel));
        if (commandService is null || componentModel is null) return;

        var state = componentModel.GetService<MuteStateService>();
        var options = (MuteOptionsPage)GetDialogPage(typeof(MuteOptionsPage));
        state.ReloadFromOptions(options);

        HushCommands.Register(commandService, state);
    }
}
