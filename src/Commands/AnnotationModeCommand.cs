using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    internal abstract class AnnotationModeCommand<T> : BaseCommand<T>
        where T : AnnotationModeCommand<T>, new()
    {
        protected abstract AnnotationMode Mode { get; }

        protected override void BeforeQueryStatus(EventArgs e)
        {
            var control = CodeShotToolWindowControl.Current;
            Command.Enabled = control?.HasPreview == true;
            Command.Checked = control?.ActiveAnnotationMode == Mode;
        }

        protected override Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            CodeShotToolWindowControl.Current?.SetAnnotationMode(Mode);
            return Task.CompletedTask;
        }
    }
}
