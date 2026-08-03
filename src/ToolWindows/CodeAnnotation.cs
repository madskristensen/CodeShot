using System.Windows;

namespace CodeShot.ToolWindows
{
    internal enum AnnotationKind
    {
        Rectangle,
        Redaction
    }

    internal sealed class CodeAnnotation
    {
        internal CodeAnnotation(AnnotationKind kind, Rect bounds)
        {
            Kind = kind;
            Bounds = bounds;
        }

        internal AnnotationKind Kind { get; }
        internal Rect Bounds { get; }
    }
}
