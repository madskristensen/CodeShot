using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    [Command(PackageIds.SaveImageCommand)]
    internal sealed class SaveImageCommand : BaseCommand<SaveImageCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
            => Command.Enabled = CodeShotToolWindowControl.Current is not null;

        protected override Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            CodeShotToolWindowControl.Current?.SaveImage();
            return Task.CompletedTask;
        }
    }
}
