namespace CodeShot
{
    [Command(PackageIds.MyCommand)]
    internal sealed class MyCommand : BaseCommand<MyCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await Package.ShowToolWindowAsync(typeof(ToolWindows.CodeShotToolWindow), 0, true, Package.DisposalToken);
        }
    }
}
