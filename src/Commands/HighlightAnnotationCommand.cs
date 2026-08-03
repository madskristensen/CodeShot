using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    [Command(PackageIds.HighlightAnnotationCommand)]
    internal sealed class HighlightAnnotationCommand : AnnotationModeCommand<HighlightAnnotationCommand>
    {
        protected override AnnotationMode Mode => AnnotationMode.Highlight;
    }
}
