namespace CodeShot
{
    [Command(PackageIds.ShowCodeShotWindowCommand)]
    internal sealed class ShowCodeShotWindowCommand : BaseCommand<ShowCodeShotWindowCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await Package.ShowToolWindowAsync(typeof(ToolWindows.CodeShotToolWindow), 0, true, Package.DisposalToken);
        }
    }
}
