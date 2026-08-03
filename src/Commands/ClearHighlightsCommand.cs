using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    [Command(PackageIds.ClearHighlightsCommand)]
    internal sealed class ClearHighlightsCommand : BaseCommand<ClearHighlightsCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            Command.Enabled = CodeShotToolWindowControl.Current?.HasHighlights == true;
        }

        protected override Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            CodeShotToolWindowControl.Current?.ClearHighlights();
            return Task.CompletedTask;
        }
    }
}
