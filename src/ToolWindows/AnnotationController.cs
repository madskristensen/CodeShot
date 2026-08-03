using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CodeShot.ToolWindows
{
    internal sealed class AnnotationController
    {
        private const double MinimumAnnotationSize = 3;

        private readonly FrameworkElement _surface;
        private readonly Canvas _layer;
        private readonly Action<string> _setStatus;
        private readonly List<CodeAnnotation> _annotations = new List<CodeAnnotation>();
        private Point? _start;
        private AnnotationKind? _draftKind;
        private System.Windows.Shapes.Rectangle? _draft;

        internal AnnotationController(FrameworkElement surface, Canvas layer, Action<string> setStatus)
        {
            _surface = surface;
            _layer = layer;
            _setStatus = setStatus;
        }

        internal AnnotationMode Mode { get; private set; }
        internal bool HasAnnotations => _annotations.Count > 0;
        internal bool HasRedactions => _annotations.Exists(annotation => annotation.Kind == AnnotationKind.Redaction);

        internal void SetMode(AnnotationMode mode)
        {
            CancelDraft();
            Mode = mode;
            _surface.Cursor = mode switch
            {
                AnnotationMode.Rectangle => Cursors.Cross,
                AnnotationMode.Redact => Cursors.Cross,
                AnnotationMode.Eraser => Cursors.Hand,
                _ => Cursors.Arrow
            };
            _setStatus(mode switch
            {
                AnnotationMode.Rectangle => "Rectangle tool active. Drag across the code to draw.",
                AnnotationMode.Redact => "Redact tool active. Drag across sensitive content to cover it.",
                AnnotationMode.Eraser => "Eraser active. Click an annotation to remove it.",
                _ => "Select mode active. Click a line to highlight it."
            });
        }

        internal bool HandleMouseDown(MouseButtonEventArgs e)
        {
            if (Mode == AnnotationMode.Select)
            {
                return false;
            }

            var point = Clamp(e.GetPosition(_surface));

            if (Mode == AnnotationMode.Eraser)
            {
                Erase(point);
                e.Handled = true;
                return true;
            }

            var kind = Mode == AnnotationMode.Redact
                ? AnnotationKind.Redaction
                : AnnotationKind.Rectangle;

            _start = point;
            _draftKind = kind;
            _draft = CreateElement(new CodeAnnotation(kind, new Rect(point, point)), true);
            _layer.Children.Add(_draft);
            _surface.CaptureMouse();
            e.Handled = true;
            return true;
        }

        internal void HandleMouseMove(MouseEventArgs e)
        {
            if (_start is null || _draft is null || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            SetElementBounds(_draft, GetBounds(_start.Value, Clamp(e.GetPosition(_surface))));
            e.Handled = true;
        }

        internal void HandleMouseUp(MouseButtonEventArgs e)
        {
            if (_start is null || _draftKind is null)
            {
                return;
            }

            var bounds = GetBounds(_start.Value, Clamp(e.GetPosition(_surface)));
            var kind = _draftKind.Value;
            CancelDraft();

            if (bounds.Width >= MinimumAnnotationSize && bounds.Height >= MinimumAnnotationSize)
            {
                _annotations.Add(new CodeAnnotation(kind, bounds));
                Refresh();
                _setStatus(kind == AnnotationKind.Redaction ? "Redaction added." : "Rectangle added.");
            }
            else
            {
                _setStatus("Drag across the code to add an annotation.");
            }

            e.Handled = true;
        }

        internal void Clear()
        {
            if (_annotations.Count == 0)
            {
                return;
            }

            Reset();
            _setStatus("Annotations cleared.");
        }

        internal void Reset()
        {
            CancelDraft();
            _annotations.Clear();
            Refresh();
        }

        internal void Refresh()
        {
            _layer.Children.Clear();

            foreach (var annotation in _annotations)
            {
                _layer.Children.Add(CreateElement(annotation, false));
            }
        }

        private void CancelDraft()
        {
            _start = null;
            _draftKind = null;

            if (_draft is not null)
            {
                _layer.Children.Remove(_draft);
                _draft = null;
            }

            if (ReferenceEquals(Mouse.Captured, _surface))
            {
                _surface.ReleaseMouseCapture();
            }
        }

        private void Erase(Point point)
        {
            for (var index = _annotations.Count - 1; index >= 0; index--)
            {
                if (_annotations[index].Bounds.Contains(point))
                {
                    _annotations.RemoveAt(index);
                    Refresh();
                    _setStatus("Annotation removed.");
                    return;
                }
            }

            _setStatus("No annotation at that point.");
        }

        private Point Clamp(Point point)
            => new Point(
                Math.Max(0, Math.Min(_surface.ActualWidth, point.X)),
                Math.Max(0, Math.Min(_surface.ActualHeight, point.Y)));

        private static Rect GetBounds(Point start, Point end)
            => new Rect(
                new Point(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y)),
                new Point(Math.Max(start.X, end.X), Math.Max(start.Y, end.Y)));

        private static System.Windows.Shapes.Rectangle CreateElement(CodeAnnotation annotation, bool isDraft)
        {
            var rectangle = new System.Windows.Shapes.Rectangle
            {
                IsHitTestVisible = false,
                Opacity = isDraft ? 0.7 : 1
            };

            if (annotation.Kind == AnnotationKind.Redaction)
            {
                rectangle.Fill = Brushes.Black;
            }
            else
            {
                rectangle.Fill = Brushes.Transparent;
                rectangle.Stroke = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
                rectangle.StrokeThickness = 3;
            }

            SetElementBounds(rectangle, annotation.Bounds);
            return rectangle;
        }

        private static void SetElementBounds(System.Windows.Shapes.Rectangle rectangle, Rect bounds)
        {
            rectangle.Width = bounds.Width;
            rectangle.Height = bounds.Height;
            Canvas.SetLeft(rectangle, bounds.Left);
            Canvas.SetTop(rectangle, bounds.Top);
        }
    }
}
