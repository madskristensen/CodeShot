using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    [Command(PackageIds.RedoAnnotationCommand)]
    internal sealed class RedoAnnotationCommand : BaseCommand<RedoAnnotationCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            Command.Enabled = CodeShotToolWindowControl.Current?.CanRedoAnnotation == true;
        }

        protected override Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            CodeShotToolWindowControl.Current?.RedoAnnotation();
            return Task.CompletedTask;
        }
    }
}
