using CodeShot.ToolWindows;
using Microsoft.VisualStudio.Shell.Interop;

namespace CodeShot.Commands
{
    [Command(PackageIds.CaptureMenuCommand)]
    internal sealed class CaptureMenuCommand : BaseCommand<CaptureMenuCommand>
    {
        private const int CaptureDelaySeconds = 5;

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var visualStudioMainWindow = MenuCapture.GetVisualStudioMainWindow();
                CodeShotToolWindowControl.StartMenuCaptureCountdown(CaptureDelaySeconds);

                // The delay and pixel capture must not resume on Visual Studio's UI thread. A modal
                // WinForms or WPF dialog owns that thread until it closes, but its pixels still need
                // to be copied while it is visible.
                var snapshot = await Task.Run(
                    async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(CaptureDelaySeconds)).ConfigureAwait(false);
                        return await MenuCapture.CaptureAsync(visualStudioMainWindow).ConfigureAwait(false);
                    });
                if (snapshot is null)
                {
                    await VS.MessageBox.ShowAsync(
                        Vsix.Name,
                        "No Visual Studio menu, context menu, or modal dialog was found when the capture timer expired.",
                        icon: OLEMSGICON.OLEMSGICON_WARNING,
                        buttons: OLEMSGBUTTON.OLEMSGBUTTON_OK);
                    return;
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                await CodeShotToolWindowControl.CopyImageToClipboardAsync(snapshot.Image);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                CodeShotToolWindowControl.ShowCapturedImageWhenReady(snapshot);
                await CodeShotToolWindow.ShowAsync();
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
                await VS.MessageBox.ShowErrorAsync(
                    Vsix.Name,
                    "The foreground Visual Studio UI could not be captured. Check ActivityLog for details.");
            }
            finally
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                CodeShotToolWindowControl.HideMenuCaptureCountdown();
                CodeShotToolWindowControl.HideLoading();
            }
        }
    }
}
