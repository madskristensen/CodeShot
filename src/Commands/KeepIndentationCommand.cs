using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    [Command(PackageIds.KeepIndentationCommand)]
    internal sealed class KeepIndentationCommand : BaseCommand<KeepIndentationCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            var control = CodeShotToolWindowControl.Current;
            Command.Enabled = control?.SupportsCodeFormatting == true;
            Command.Checked = control?.KeepOriginalIndentation == true;
        }

        protected override Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            var control = CodeShotToolWindowControl.Current;

            if (control is not null)
            {
                control.KeepOriginalIndentation = !control.KeepOriginalIndentation;
            }

            return Task.CompletedTask;
        }
    }
}
