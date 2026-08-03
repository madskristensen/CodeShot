using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    [Command(PackageIds.SelectAnnotationCommand)]
    internal sealed class SelectAnnotationCommand : AnnotationModeCommand<SelectAnnotationCommand>
    {
        protected override AnnotationMode Mode => AnnotationMode.Select;
    }
}
