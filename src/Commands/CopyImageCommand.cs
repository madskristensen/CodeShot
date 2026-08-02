using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    [Command(PackageIds.CopyImageCommand)]
    internal sealed class CopyImageCommand : BaseCommand<CopyImageCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
            => Command.Enabled = CodeShotToolWindowControl.Current is not null;

        protected override Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            CodeShotToolWindowControl.Current?.CopyImage();
            return Task.CompletedTask;
        }
    }
}
