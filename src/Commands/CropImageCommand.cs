using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    [Command(PackageIds.CropImageCommand)]
    internal sealed class CropImageCommand : BaseCommand<CropImageCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
            => Command.Enabled = CodeShotToolWindowControl.Current?.CanCrop == true;

        protected override Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            CodeShotToolWindowControl.Current?.BeginCrop();
            return Task.CompletedTask;
        }
    }
}
