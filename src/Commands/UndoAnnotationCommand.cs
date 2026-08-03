using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    [Command(PackageIds.UndoAnnotationCommand)]
    internal sealed class UndoAnnotationCommand : BaseCommand<UndoAnnotationCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            Command.Enabled = CodeShotToolWindowControl.Current?.CanUndoAnnotation == true;
        }

        protected override Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            CodeShotToolWindowControl.Current?.UndoAnnotation();
            return Task.CompletedTask;
        }
    }
}
