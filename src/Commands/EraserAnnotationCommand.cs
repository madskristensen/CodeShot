using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    [Command(PackageIds.EraserAnnotationCommand)]
    internal sealed class EraserAnnotationCommand : AnnotationModeCommand<EraserAnnotationCommand>
    {
        protected override AnnotationMode Mode => AnnotationMode.Eraser;
    }
}
