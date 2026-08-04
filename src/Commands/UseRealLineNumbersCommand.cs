using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    [Command(PackageIds.UseRealLineNumbersCommand)]
    internal sealed class UseRealLineNumbersCommand : BaseCommand<UseRealLineNumbersCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            var control = CodeShotToolWindowControl.Current;

            // Numbering only matters for code previews when the numbers are visible.
            Command.Enabled = control?.SupportsCodeFormatting == true && control.ShowLineNumbers;
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
