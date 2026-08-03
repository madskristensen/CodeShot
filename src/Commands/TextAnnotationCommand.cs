using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    [Command(PackageIds.TextAnnotationCommand)]
    internal sealed class TextAnnotationCommand : AnnotationModeCommand<TextAnnotationCommand>
    {
        protected override AnnotationMode Mode => AnnotationMode.Text;
    }
}
