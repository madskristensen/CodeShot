using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CodeShot.ToolWindows
{
    internal sealed class AnnotationController
    {
        private const double MinimumAnnotationSize = 3;
        private const int HistoryLimit = 100;

        private readonly FrameworkElement _surface;
        private readonly Canvas _layer;
        private readonly Action<string> _setStatus;
        private readonly List<CodeAnnotation> _annotations = new List<CodeAnnotation>();
        private readonly List<AnnotationChange> _undoHistory = new List<AnnotationChange>();
        private readonly List<AnnotationChange> _redoHistory = new List<AnnotationChange>();
        private Point? _start;
        private AnnotationKind? _draftKind;
        private FrameworkElement? _draft;
        private TextBox? _textEditor;
        private Point _textPosition;

        internal AnnotationController(FrameworkElement surface, Canvas layer, Action<string> setStatus)
        {
            _surface = surface;
            _layer = layer;
            _setStatus = setStatus;
        }

        internal AnnotationMode Mode { get; private set; }
        internal bool HasAnnotations => _annotations.Count > 0;
        internal bool HasRedactions => _annotations.Exists(annotation => annotation.Kind == AnnotationKind.Redaction);
        internal bool CanUndo => _textEditor is not null ? _textEditor.CanUndo : _undoHistory.Count > 0;
        internal bool CanRedo => _textEditor is not null ? _textEditor.CanRedo : _redoHistory.Count > 0;

        internal bool IsTextEditorInput(object source)
            => _textEditor is not null
                && source is DependencyObject dependencyObject
                && (ReferenceEquals(_textEditor, dependencyObject) || _textEditor.IsAncestorOf(dependencyObject));

        internal void SetMode(AnnotationMode mode)
        {
            CommitTextEdit();
            CancelDraft();
            Mode = mode;
            _surface.Cursor = mode switch
            {
                AnnotationMode.Rectangle => Cursors.Cross,
                AnnotationMode.Arrow => Cursors.Cross,
                AnnotationMode.Highlight => Cursors.Cross,
                AnnotationMode.Text => Cursors.IBeam,
                AnnotationMode.Redact => Cursors.Cross,
                AnnotationMode.Eraser => Cursors.Hand,
                _ => Cursors.Arrow
            };
            _setStatus(mode switch
            {
                AnnotationMode.Rectangle => "Rectangle tool active. Drag across the code to draw.",
                AnnotationMode.Arrow => "Arrow tool active. Drag from the subject toward the point of interest.",
                AnnotationMode.Highlight => "Highlighter active. Drag across an expression to emphasize it.",
                AnnotationMode.Text => "Text tool active. Click where the note should appear.",
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

            if (Mode == AnnotationMode.Text)
            {
                BeginTextEdit(point);
                e.Handled = true;
                return true;
            }

            if (Mode == AnnotationMode.Eraser)
            {
                Erase(point);
                e.Handled = true;
                return true;
            }

            var kind = Mode switch
            {
                AnnotationMode.Arrow => AnnotationKind.Arrow,
                AnnotationMode.Highlight => AnnotationKind.Highlight,
                AnnotationMode.Redact => AnnotationKind.Redaction,
                _ => AnnotationKind.Rectangle
            };

            _start = point;
            _draftKind = kind;
            _draft = CreateElement(new CodeAnnotation(kind, point, point), true);
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

            _layer.Children.Remove(_draft);
            _draft = CreateElement(new CodeAnnotation(_draftKind!.Value, _start.Value, Clamp(e.GetPosition(_surface))), true);
            _layer.Children.Add(_draft);
            e.Handled = true;
        }

        internal void HandleMouseUp(MouseButtonEventArgs e)
        {
            if (_start is null || _draftKind is null)
            {
                return;
            }

            var end = Clamp(e.GetPosition(_surface));
            var bounds = GetBounds(_start.Value, end);
            var kind = _draftKind.Value;
            var start = _start.Value;
            CancelDraft();

            var isLargeEnough = kind == AnnotationKind.Arrow
                ? (end - start).Length >= MinimumAnnotationSize
                : bounds.Width >= MinimumAnnotationSize && bounds.Height >= MinimumAnnotationSize;

            if (isLargeEnough)
            {
                AddAnnotation(new CodeAnnotation(kind, start, end));
                Refresh();
                _setStatus(kind switch
                {
                    AnnotationKind.Arrow => "Arrow added.",
                    AnnotationKind.Highlight => "Highlight added.",
                    AnnotationKind.Redaction => "Redaction added.",
                    _ => "Rectangle added."
                });
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

            RecordChange(AnnotationChange.Clear(_annotations));
            CancelDraft();
            _annotations.Clear();
            Refresh();
            _setStatus("Annotations cleared.");
        }

        internal void Reset()
        {
            CancelTextEdit();
            CancelDraft();
            _annotations.Clear();
            _undoHistory.Clear();
            _redoHistory.Clear();
            Refresh();
        }

        internal void Undo()
        {
            if (_textEditor is not null)
            {
                if (_textEditor.CanUndo)
                {
                    _textEditor.Undo();
                }

                return;
            }

            if (CanUndo == false)
            {
                return;
            }

            CancelDraft();
            var change = PopLast(_undoHistory);
            change.Undo(_annotations);
            _redoHistory.Add(change);
            Refresh();
            _setStatus("Undid annotation change.");
        }

        internal void Redo()
        {
            if (_textEditor is not null)
            {
                if (_textEditor.CanRedo)
                {
                    _textEditor.Redo();
                }

                return;
            }

            if (CanRedo == false)
            {
                return;
            }

            CancelDraft();
            var change = PopLast(_redoHistory);
            change.Redo(_annotations);
            _undoHistory.Add(change);
            Refresh();
            _setStatus("Redid annotation change.");
        }

        internal bool CopyText()
        {
            if (_textEditor is null)
            {
                return false;
            }

            _textEditor.Copy();
            return true;
        }

        internal void HandleSurfaceSizeChanged()
        {
            if (_annotations.Count == 0 && _textEditor is null)
            {
                Refresh();
                return;
            }

            Reset();
            _setStatus("Annotations cleared because the preview layout changed.");
        }

        internal void Refresh()
        {
            _layer.Children.Clear();

            foreach (var annotation in _annotations)
            {
                _layer.Children.Add(CreateElement(annotation, false));
            }

            if (_textEditor is not null)
            {
                _layer.Children.Add(_textEditor);
                PositionElement(_textEditor, _textPosition);
            }
        }

        internal void CommitTextEdit()
        {
            if (_textEditor is null)
            {
                return;
            }

            var editor = _textEditor;
            var text = editor.Text.Trim();
            _textEditor = null;
            _layer.IsHitTestVisible = false;
            _layer.Children.Remove(editor);

            if (text.Length == 0)
            {
                _setStatus("Empty text callout discarded.");
                return;
            }

            editor.Measure(new Size(editor.MaxWidth, double.PositiveInfinity));
            var size = editor.DesiredSize;
            var end = Clamp(new Point(_textPosition.X + size.Width, _textPosition.Y + size.Height));
            AddAnnotation(new CodeAnnotation(AnnotationKind.Text, _textPosition, end, text));
            Refresh();
            _setStatus("Text callout added.");
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

        private void BeginTextEdit(Point position)
        {
            CommitTextEdit();
            _textPosition = position;
            var maxWidth = Math.Max(40, _surface.ActualWidth - position.X);
            _textEditor = new TextBox
            {
                MinWidth = Math.Min(140, maxWidth),
                MaxWidth = maxWidth,
                Padding = new Thickness(6, 4, 6, 4),
                Background = CalloutBackgroundBrush,
                Foreground = Brushes.Black,
                BorderBrush = AnnotationBrush,
                BorderThickness = new Thickness(2),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            };
            _textEditor.KeyDown += OnTextEditorKeyDown;
            _textEditor.LostKeyboardFocus += OnTextEditorLostKeyboardFocus;
            _layer.IsHitTestVisible = true;
            _layer.Children.Add(_textEditor);
            PositionElement(_textEditor, position);
            _textEditor.Focus();
        }

        private void OnTextEditorKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitTextEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelTextEdit();
                _setStatus("Text callout canceled.");
                e.Handled = true;
            }
        }

        private void OnTextEditorLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
            => CommitTextEdit();

        private void CancelTextEdit()
        {
            if (_textEditor is null)
            {
                return;
            }

            var editor = _textEditor;
            _textEditor = null;
            editor.KeyDown -= OnTextEditorKeyDown;
            editor.LostKeyboardFocus -= OnTextEditorLostKeyboardFocus;
            _layer.Children.Remove(editor);
            _layer.IsHitTestVisible = false;
        }

        private void Erase(Point point)
        {
            for (var index = _annotations.Count - 1; index >= 0; index--)
            {
                var annotation = _annotations[index];
                var isHit = annotation.Kind == AnnotationKind.Arrow
                    ? DistanceToSegment(point, annotation.Start, annotation.End) <= 8
                    : IsInsideInflatedBounds(point, annotation.Bounds);

                if (isHit)
                {
                    RecordChange(AnnotationChange.Remove(index, annotation));
                    _annotations.RemoveAt(index);
                    Refresh();
                    _setStatus("Annotation removed.");
                    return;
                }
            }

            _setStatus("No annotation at that point.");
        }

        private static bool IsInsideInflatedBounds(Point point, Rect bounds)
        {
            bounds.Inflate(6, 6);
            return bounds.Contains(point);
        }

        private static double DistanceToSegment(Point point, Point start, Point end)
        {
            var segment = end - start;
            var lengthSquared = segment.LengthSquared;

            if (lengthSquared == 0)
            {
                return (point - start).Length;
            }

            var offset = point - start;
            var projection = Math.Max(0, Math.Min(1, ((offset.X * segment.X) + (offset.Y * segment.Y)) / lengthSquared));
            var closest = start + (segment * projection);
            return (point - closest).Length;
        }

        private void AddAnnotation(CodeAnnotation annotation)
        {
            RecordChange(AnnotationChange.Add(_annotations.Count, annotation));
            _annotations.Add(annotation);
        }

        private void RecordChange(AnnotationChange change)
        {
            if (_undoHistory.Count == HistoryLimit)
            {
                _undoHistory.RemoveAt(0);
            }

            _undoHistory.Add(change);
            _redoHistory.Clear();
        }

        private static AnnotationChange PopLast(List<AnnotationChange> history)
        {
            var index = history.Count - 1;
            var change = history[index];
            history.RemoveAt(index);
            return change;
        }

        private Point Clamp(Point point)
            => new Point(
                Math.Max(0, Math.Min(_surface.ActualWidth, point.X)),
                Math.Max(0, Math.Min(_surface.ActualHeight, point.Y)));

        private static Rect GetBounds(Point start, Point end)
            => new Rect(
                new Point(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y)),
                new Point(Math.Max(start.X, end.X), Math.Max(start.Y, end.Y)));

        private static FrameworkElement CreateElement(CodeAnnotation annotation, bool isDraft)
        {
            if (annotation.Kind == AnnotationKind.Arrow)
            {
                return CreateArrow(annotation, isDraft);
            }

            if (annotation.Kind == AnnotationKind.Text)
            {
                return CreateTextCallout(annotation);
            }

            var rectangle = new Rectangle
            {
                IsHitTestVisible = false,
                Opacity = isDraft ? 0.7 : 1,
                Fill = annotation.Kind switch
                {
                    AnnotationKind.Redaction => Brushes.Black,
                    AnnotationKind.Highlight => HighlightBrush,
                    _ => Brushes.Transparent
                },
                Stroke = annotation.Kind == AnnotationKind.Rectangle ? AnnotationBrush : null,
                StrokeThickness = 3
            };

            SetElementBounds(rectangle, annotation.Bounds);
            return rectangle;
        }

        private static Path CreateArrow(CodeAnnotation annotation, bool isDraft)
        {
            const double headLength = 12;
            const double headWidth = 7;

            var direction = annotation.End - annotation.Start;
            if (direction.Length > 0)
            {
                direction.Normalize();
            }

            var perpendicular = new Vector(-direction.Y, direction.X);
            var headBase = annotation.End - (direction * headLength);
            var geometry = new StreamGeometry();

            using (var context = geometry.Open())
            {
                context.BeginFigure(annotation.Start, false, false);
                context.LineTo(annotation.End, true, false);
                context.BeginFigure(annotation.End, true, true);
                context.LineTo(headBase + (perpendicular * headWidth), true, false);
                context.LineTo(headBase - (perpendicular * headWidth), true, false);
            }

            geometry.Freeze();
            return new Path
            {
                Data = geometry,
                Stroke = AnnotationBrush,
                StrokeThickness = 3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Fill = AnnotationBrush,
                IsHitTestVisible = false,
                Opacity = isDraft ? 0.7 : 1
            };
        }

        private static Border CreateTextCallout(CodeAnnotation annotation)
        {
            var width = Math.Max(1, annotation.Bounds.Width);
            var border = new Border
            {
                Width = width,
                Background = CalloutBackgroundBrush,
                BorderBrush = AnnotationBrush,
                BorderThickness = new Thickness(2),
                Padding = new Thickness(6, 4, 6, 4),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = annotation.Text,
                    Foreground = Brushes.Black,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            PositionElement(border, annotation.Start);
            return border;
        }

        private static void SetElementBounds(Rectangle rectangle, Rect bounds)
        {
            rectangle.Width = bounds.Width;
            rectangle.Height = bounds.Height;
            Canvas.SetLeft(rectangle, bounds.Left);
            Canvas.SetTop(rectangle, bounds.Top);
        }

        private static void PositionElement(FrameworkElement element, Point position)
        {
            Canvas.SetLeft(element, position.X);
            Canvas.SetTop(element, position.Y);
        }

        private static Brush AnnotationBrush { get; } = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
        private static Brush HighlightBrush { get; } = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xEB, 0x3B));
        private static Brush CalloutBackgroundBrush { get; } = new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xE1));

        private sealed class AnnotationChange
        {
            private readonly AnnotationChangeKind _kind;
            private readonly int _index;
            private readonly IReadOnlyList<CodeAnnotation> _annotations;

            private AnnotationChange(AnnotationChangeKind kind, int index, IReadOnlyList<CodeAnnotation> annotations)
            {
                _kind = kind;
                _index = index;
                _annotations = annotations;
            }

            internal static AnnotationChange Add(int index, CodeAnnotation annotation)
                => new AnnotationChange(AnnotationChangeKind.Add, index, new[] { annotation });

            internal static AnnotationChange Remove(int index, CodeAnnotation annotation)
                => new AnnotationChange(AnnotationChangeKind.Remove, index, new[] { annotation });

            internal static AnnotationChange Clear(IReadOnlyList<CodeAnnotation> annotations)
                => new AnnotationChange(AnnotationChangeKind.Clear, 0, new List<CodeAnnotation>(annotations));

            internal void Undo(List<CodeAnnotation> annotations)
            {
                switch (_kind)
                {
                    case AnnotationChangeKind.Add:
                        annotations.RemoveAt(_index);
                        break;
                    case AnnotationChangeKind.Remove:
                        annotations.Insert(_index, _annotations[0]);
                        break;
                    case AnnotationChangeKind.Clear:
                        annotations.AddRange(_annotations);
                        break;
                }
            }

            internal void Redo(List<CodeAnnotation> annotations)
            {
                switch (_kind)
                {
                    case AnnotationChangeKind.Add:
                        annotations.Insert(_index, _annotations[0]);
                        break;
                    case AnnotationChangeKind.Remove:
                        annotations.RemoveAt(_index);
                        break;
                    case AnnotationChangeKind.Clear:
                        annotations.Clear();
                        break;
                }
            }
        }

        private enum AnnotationChangeKind
        {
            Add,
            Remove,
            Clear
        }
    }
}
