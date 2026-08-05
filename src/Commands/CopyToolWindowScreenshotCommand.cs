using CodeShot.ToolWindows;
using Microsoft.VisualStudio.Shell.Interop;

namespace CodeShot.Commands
{
    [Command(PackageIds.CopyToolWindowScreenshotCommand)]
    internal sealed class CopyToolWindowScreenshotCommand : BaseCommand<CopyToolWindowScreenshotCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                CodeShotToolWindowControl.ShowLoading("Capturing tool window...");
                var snapshot = await ToolWindowCapture.CaptureCurrentAsync();
                if (snapshot is null)
                {
                    await VS.MessageBox.ShowAsync(
                        Vsix.Name,
                        "Visual Studio did not provide usable bounds, or the tool window is too large to capture safely.",
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
                    "The tool window could not be captured. Check ActivityLog for details.");
            }
            finally
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                CodeShotToolWindowControl.HideLoading();
            }
        }
    }
}
