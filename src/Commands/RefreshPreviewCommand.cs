using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    [Command(PackageIds.RefreshPreviewCommand)]
    internal sealed class RefreshPreviewCommand : BaseCommand<RefreshPreviewCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
            => Command.Enabled = CodeShotToolWindowControl.Current is not null;

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            CodeShotToolWindowControl.Current?.Refresh();
        }
    }
}
