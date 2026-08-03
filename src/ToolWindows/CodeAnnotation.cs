using System.Windows;

namespace CodeShot.ToolWindows
{
    internal enum AnnotationKind
    {
        Rectangle,
        Arrow,
        Highlight,
        Redaction
    }

    internal sealed class CodeAnnotation
    {
        internal CodeAnnotation(AnnotationKind kind, Rect bounds)
            : this(kind, bounds.TopLeft, bounds.BottomRight)
        {
        }

        internal CodeAnnotation(AnnotationKind kind, Point start, Point end)
        {
            Kind = kind;
            Start = start;
            End = end;
        }

        internal AnnotationKind Kind { get; }
        internal Point Start { get; }
        internal Point End { get; }
        internal Rect Bounds => new Rect(
            new Point(Math.Min(Start.X, End.X), Math.Min(Start.Y, End.Y)),
            new Point(Math.Max(Start.X, End.X), Math.Max(Start.Y, End.Y)));
    }
}
