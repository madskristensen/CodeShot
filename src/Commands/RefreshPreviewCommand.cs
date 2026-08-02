using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    [Command(PackageIds.RefreshPreviewCommand)]
    internal sealed class RefreshPreviewCommand : BaseCommand<RefreshPreviewCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
            => Command.Enabled = CodeShotToolWindowControl.Current is not null;

        protected override Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            CodeShotToolWindowControl.Current?.Refresh();
            return Task.CompletedTask;
        }
    }
}
