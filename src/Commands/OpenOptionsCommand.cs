namespace CodeShot.Commands
{
    [Command(PackageIds.OpenOptionsCommand)]
    internal sealed class OpenOptionsCommand : BaseCommand<OpenOptionsCommand>
    {
        protected override Task ExecuteAsync(OleMenuCmdEventArgs e)
            => VS.Settings.OpenAsync<OptionsProvider.GeneralOptions>();
    }
}
