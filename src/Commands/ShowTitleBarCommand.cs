using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    [Command(PackageIds.ShowTitleBarCommand)]
    internal sealed class ShowTitleBarCommand : BaseCommand<ShowTitleBarCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            var control = CodeShotToolWindowControl.Current;
            Command.Enabled = control is not null;
            Command.Checked = control?.ShowTitleBar == true;
        }

        protected override Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            var control = CodeShotToolWindowControl.Current;

            if (control is not null)
            {
                control.ShowTitleBar = !control.ShowTitleBar;
            }

            return Task.CompletedTask;
        }
    }
}
