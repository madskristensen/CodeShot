using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    [Command(PackageIds.CopyImageCommand)]
    internal sealed class CopyImageCommand : BaseCommand<CopyImageCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
            => Command.Enabled = CodeShotToolWindowControl.Current is not null;

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            CodeShotToolWindowControl.Current?.CopyImage();
        }
    }
}
