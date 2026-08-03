using CodeShot.ToolWindows;

namespace CodeShot.Commands
{
    [Command(PackageIds.RedactAnnotationCommand)]
    internal sealed class RedactAnnotationCommand : AnnotationModeCommand<RedactAnnotationCommand>
    {
        protected override AnnotationMode Mode => AnnotationMode.Redact;
    }
}
