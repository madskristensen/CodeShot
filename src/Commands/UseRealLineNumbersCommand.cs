using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    [Command(PackageIds.UseRealLineNumbersCommand)]
    internal sealed class UseRealLineNumbersCommand : BaseCommand<UseRealLineNumbersCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            var control = CodeShotToolWindowControl.Current;

            // Numbering only matters when the numbers are visible in the first place.
            Command.Enabled = control?.ShowLineNumbers == true;
            Command.Checked = control?.UseRealLineNumbers == true;
        }

        protected override Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            var control = CodeShotToolWindowControl.Current;

            if (control is not null)
            {
                control.UseRealLineNumbers = !control.UseRealLineNumbers;
            }

            return Task.CompletedTask;
        }
    }
}
