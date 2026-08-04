namespace CodeShot
{
    [Command(PackageIds.ShowCodeShotWindowCommand)]
    internal sealed class ShowCodeShotWindowCommand : BaseCommand<ShowCodeShotWindowCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ToolWindows.CodeShotToolWindow.ShowAsync();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            ToolWindows.CodeShotToolWindowControl.CopyWhenReady();
        }
    }
}
