using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    [Command(PackageIds.ShowLineNumbersCommand)]
    internal sealed class ShowLineNumbersCommand : BaseCommand<ShowLineNumbersCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            var control = CodeShotToolWindowControl.Current;
            Command.Enabled = control is not null;
            Command.Checked = control?.ShowLineNumbers == true;
        }

        protected override Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            var control = CodeShotToolWindowControl.Current;

            if (control is not null)
            {
                control.ShowLineNumbers = !control.ShowLineNumbers;
            }

            return Task.CompletedTask;
        }
    }
}
