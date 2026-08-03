using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    [Command(PackageIds.RectangleAnnotationCommand)]
    internal sealed class RectangleAnnotationCommand : AnnotationModeCommand<RectangleAnnotationCommand>
    {
        protected override AnnotationMode Mode => AnnotationMode.Rectangle;
    }
}
