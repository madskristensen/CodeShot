global using System;
global using Community.VisualStudio.Toolkit;
global using Microsoft.VisualStudio.Shell;
global using Task = System.Threading.Tasks.Task;
using System.Runtime.InteropServices;
using System.Threading;

namespace CodeShot
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(CodeShot.ToolWindows.CodeShotToolWindow.Pane), Width = 600, Height = 500)]
    [ProvideOptionPage(typeof(OptionsProvider.GeneralOptions), Vsix.Name, "General", 0, 0, true, SupportsProfiles = true)]
    // Declares the tool window's GUID as a key binding scope so the shortcuts scoped to it in the
    // .vsct are honoured, and so the scope shows up as "CodeShot" in Tools - Options - Keyboard.
    [ProvideKeyBindingTable(PackageGuids.CodeShotToolWindowString, KeyBindingTableNameResourceId)]
    [Guid(PackageGuids.CodeShotString)]
    public sealed class CodeShotPackage : ToolkitPackage
    {
        private const int KeyBindingTableNameResourceId = 200;
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await this.RegisterCommandsAsync();
            this.RegisterToolWindows();
            CodeShot.ToolWindows.FontCatalog.PrimeAsync().FileAndForget($"{Vsix.Name}/{nameof(InitializeAsync)}");
        }
    }
}