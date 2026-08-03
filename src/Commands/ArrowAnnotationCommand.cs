using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    [Command(PackageIds.ArrowAnnotationCommand)]
    internal sealed class ArrowAnnotationCommand : AnnotationModeCommand<ArrowAnnotationCommand>
    {
        protected override AnnotationMode Mode => AnnotationMode.Arrow;
    }
}
