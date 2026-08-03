using System.Windows;

namespace CodeShot.ToolWindows
{
    internal enum AnnotationKind
    {
        Rectangle,
        Arrow,
        Highlight,
        Text,
        Redaction
    }

    internal sealed class CodeAnnotation
    {
        internal CodeAnnotation(AnnotationKind kind, Rect bounds)
            : this(kind, bounds.TopLeft, bounds.BottomRight)
        {
        }

        internal CodeAnnotation(AnnotationKind kind, Point start, Point end)
            : this(kind, start, end, string.Empty)
        {
        }

        internal CodeAnnotation(AnnotationKind kind, Point start, Point end, string text)
        {
            Kind = kind;
            Start = start;
            End = end;
            Text = text;
        }

        internal AnnotationKind Kind { get; }
        internal Point Start { get; }
        internal Point End { get; }
        internal string Text { get; }
        internal Rect Bounds => new Rect(
            new Point(Math.Min(Start.X, End.X), Math.Min(Start.Y, End.Y)),
            new Point(Math.Max(Start.X, End.X), Math.Max(Start.Y, End.Y)));
    }
}
