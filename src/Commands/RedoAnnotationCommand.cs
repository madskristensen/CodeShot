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

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            CodeShotToolWindowControl.Current?.RedoAnnotation();
        }
    }
}
