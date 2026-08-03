using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    [Command(PackageIds.ClearAnnotationsCommand)]
    internal sealed class ClearAnnotationsCommand : BaseCommand<ClearAnnotationsCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            Command.Enabled = CodeShotToolWindowControl.Current?.HasAnnotations == true;
        }

        protected override Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            CodeShotToolWindowControl.Current?.ClearAnnotations();
            return Task.CompletedTask;
        }
    }
}
