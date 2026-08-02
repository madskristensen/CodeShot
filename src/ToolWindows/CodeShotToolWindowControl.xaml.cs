using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodeShot.ToolWindows
{
    public partial class CodeShotToolWindowControl : UserControl
    {
        private string _selectedCode = string.Empty;
        private FlowDocument? _classifiedDocument;
        private int _selectionStartLine;
        private IWpfTextView? _trackedTextView;
        private bool _isRefreshingSelection;

        public CodeShotToolWindowControl()
        {
            InitializeComponent();
            ApplyTheme();
            VSColorTheme.ThemeChanged += OnThemeChanged;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnThemeChanged(ThemeChangedEventArgs e)
        {
            _ = RunSafeAsync(
                async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    ApplyTheme();
                    await RefreshFromSelectionAsync();
                },
                "Could not apply theme updates.");
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VSColorTheme.ThemeChanged -= OnThemeChanged;
            DetachFromSelectionChanges();
            Unloaded -= OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            _ = RunSafeAsync(
                async () => await RefreshFromSelectionAsync(),
                "Could not initialize the preview.");
        }

        private void RefreshButton_OnClick(object sender, RoutedEventArgs e)
        {
            _ = RunSafeAsync(
                async () => await RefreshFromSelectionAsync(),
                "Could not refresh from the current selection.");
        }

        private void PreviewOptionChanged(object sender, RoutedEventArgs e)
        {
            if (TitleBarBorder is null || ShowTitleBarCheckBox is null)
            {
                return;
            }

            TitleBarBorder.Visibility = ShowTitleBarCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            UpdatePreviewText();
        }

        private void CopyButton_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var snapshot = RenderSnapshot();
                if (snapshot is null)
                {
                    StatusText.Text = "Nothing to copy yet.";
                    return;
                }

                Clipboard.SetImage(snapshot);
                StatusText.Text = "Copied screenshot to clipboard.";
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
                StatusText.Text = "Copy failed. Check ActivityLog for details.";
            }
        }

        private void SaveButton_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var snapshot = RenderSnapshot();
                if (snapshot is null)
                {
                    StatusText.Text = "Nothing to save yet.";
                    return;
                }

                var dialog = new SaveFileDialog
                {
                    Filter = "PNG image (*.png)|*.png",
                    AddExtension = true,
                    DefaultExt = ".png",
                    FileName = "codeshot.png"
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(snapshot));

                using var stream = File.Create(dialog.FileName);
                encoder.Save(stream);
                StatusText.Text = $"Saved screenshot to '{dialog.FileName}'.";
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
                StatusText.Text = "Save failed. Check ActivityLog for details.";
            }
        }

        private async System.Threading.Tasks.Task RefreshFromSelectionAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (_isRefreshingSelection)
            {
                return;
            }

            _isRefreshingSelection = true;

            try
            {
                var dte = await VS.GetServiceAsync<DTE, DTE2>();
                var textView = GetActiveTextView();

                if (textView is null || textView.Selection.IsEmpty || textView.Selection.SelectedSpans.Count == 0)
                {
                    ClearSelectionPreview();
                    return;
                }

                AttachToSelectionChanges(textView);

                var selectedSpans = GetNormalizedSelectionSpans(textView);

                if (selectedSpans.Count == 0)
                {
                    ClearSelectionPreview();
                    return;
                }

                _selectedCode = string.Join(Environment.NewLine, selectedSpans.Select(span => span.GetText()));
                _selectionStartLine = selectedSpans[0].Start.GetContainingLine().LineNumber + 1;
                _classifiedDocument = BuildClassifiedDocument(textView, selectedSpans);

                var documentName = Path.GetFileName(dte?.ActiveDocument?.FullName ?? string.Empty);
                TitleText.Text = string.IsNullOrWhiteSpace(documentName) ? "CodeShot" : documentName;
                StatusText.Text = _classifiedDocument is null
                    ? "Preview updated from current selection (plain text fallback)."
                    : "Preview updated from current selection.";

                UpdatePreviewText();
            }
            finally
            {
                _isRefreshingSelection = false;
            }
        }

        private void ClearSelectionPreview()
        {
            _selectedCode = string.Empty;
            _classifiedDocument = null;
            _selectionStartLine = 0;
            TitleText.Text = "No selection";
            StatusText.Text = "Select code in the editor to update preview.";
            UpdatePreviewText();
        }

        private void UpdatePreviewText()
        {
            if (PreviewRichText is null || LineNumbersText is null)
            {
                return;
            }

            var normalized = NormalizeLineEndings(_selectedCode);
            var lines = normalized.Split(new[] { '\n' }, StringSplitOptions.None);

            if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
            {
                lines = lines.Take(lines.Length - 1).ToArray();
            }

            if (lines.Length == 0)
            {
                SetPreviewPlainText(string.Empty);
                LineNumbersText.Text = string.Empty;
                return;
            }

            if (_classifiedDocument is not null)
            {
                PreviewRichText.Document = _classifiedDocument;
            }
            else
            {
                SetPreviewPlainText(string.Join(Environment.NewLine, lines));
            }

            if (ShowLineNumbersCheckBox.IsChecked == true)
            {
                var start = Math.Max(1, _selectionStartLine);
                var width = (start + lines.Length - 1).ToString().Length;
                var numberedLines = lines.Select((_, index) => (start + index).ToString().PadLeft(width));
                LineNumbersText.Text = string.Join(Environment.NewLine, numberedLines);
                LineNumbersText.Visibility = Visibility.Visible;
                return;
            }

            LineNumbersText.Text = string.Empty;
            LineNumbersText.Visibility = Visibility.Collapsed;
        }

        private RenderTargetBitmap? RenderSnapshot()
        {
            CaptureSurface.UpdateLayout();

            if (CaptureSurface.ActualWidth <= 0 || CaptureSurface.ActualHeight <= 0)
            {
                return null;
            }

            var dpi = VisualTreeHelper.GetDpi(CaptureSurface);
            var pixelWidth = Math.Max(1, (int)Math.Ceiling(CaptureSurface.ActualWidth * dpi.DpiScaleX));
            var pixelHeight = Math.Max(1, (int)Math.Ceiling(CaptureSurface.ActualHeight * dpi.DpiScaleY));

            var renderTarget = new RenderTargetBitmap(
                pixelWidth,
                pixelHeight,
                96 * dpi.DpiScaleX,
                96 * dpi.DpiScaleY,
                PixelFormats.Pbgra32);

            var drawingVisual = new DrawingVisual();
            using (var context = drawingVisual.RenderOpen())
            {
                var brush = new VisualBrush(CaptureSurface);
                context.DrawRectangle(brush, null, new Rect(new Point(), new Size(CaptureSurface.ActualWidth, CaptureSurface.ActualHeight)));
            }

            renderTarget.Render(drawingVisual);
            return renderTarget;
        }

        private static string NormalizeLineEndings(string value)
            => value.Replace("\r\n", "\n").Replace('\r', '\n');

        private void ApplyTheme()
        {
            if (RootGrid is null)
            {
                return;
            }

            var toolWindowBackground = ToMediaColor(VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey));
            var toolWindowText = ToMediaColor(VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowTextColorKey));
            var (editorForegroundBrush, editorBackgroundBrush) = GetEditorTextBrushes(toolWindowText, toolWindowBackground);
            var editorForeground = ((SolidColorBrush)editorForegroundBrush).Color;
            var editorBackground = ((SolidColorBrush)editorBackgroundBrush).Color;
            var isDark = IsDark(toolWindowBackground);

            var chromeBackground = isDark
                ? Blend(toolWindowBackground, Colors.White, 0.12)
                : Blend(toolWindowBackground, Colors.Black, 0.08);
            var previewBackground = editorBackground;
            var captureBackground = isDark
                ? Blend(toolWindowBackground, Colors.White, 0.22)
                : Blend(toolWindowBackground, Colors.Black, 0.16);
            var subtleBorder = isDark
                ? Blend(toolWindowText, toolWindowBackground, 0.72)
                : Blend(toolWindowText, toolWindowBackground, 0.82);

            RootGrid.Background = new SolidColorBrush(toolWindowBackground);
            CommandBarBorder.Background = new SolidColorBrush(chromeBackground);
            CommandBarBorder.BorderBrush = new SolidColorBrush(subtleBorder);

            CaptureSurface.Background = new SolidColorBrush(captureBackground);
            SnapshotFrame.Background = new SolidColorBrush(previewBackground);
            SnapshotFrame.BorderBrush = new SolidColorBrush(subtleBorder);
            TitleBarBorder.Background = new SolidColorBrush(chromeBackground);
            TitleBarBorder.BorderBrush = new SolidColorBrush(subtleBorder);
            TitleBarBorder.BorderThickness = new Thickness(0, 0, 0, 1);

            PreviewRichText.Background = Brushes.Transparent;
            PreviewRichText.Foreground = editorForegroundBrush;
            LineNumbersText.Foreground = new SolidColorBrush(Blend(editorForeground, editorBackground, 0.55));
            TitleText.Foreground = new SolidColorBrush(toolWindowText);
            StatusText.Foreground = new SolidColorBrush(Blend(toolWindowText, toolWindowBackground, 0.35));
            ShowLineNumbersCheckBox.Foreground = new SolidColorBrush(toolWindowText);
            ShowTitleBarCheckBox.Foreground = new SolidColorBrush(toolWindowText);

            StyleButton(RefreshButton, chromeBackground, toolWindowText, subtleBorder);
            StyleButton(CopyButton, chromeBackground, toolWindowText, subtleBorder);
            StyleButton(SaveButton, chromeBackground, toolWindowText, subtleBorder);
        }

        private static void StyleButton(Button button, Color background, Color foreground, Color border)
        {
            if (button is null)
            {
                return;
            }

            button.Background = new SolidColorBrush(background);
            button.Foreground = new SolidColorBrush(foreground);
            button.BorderBrush = new SolidColorBrush(border);
            button.BorderThickness = new Thickness(1);
        }

        private static bool IsDark(Color color)
            => ((color.R * 299) + (color.G * 587) + (color.B * 114)) / 1000.0 < 140;

        private static Color Blend(Color from, Color to, double amount)
        {
            var clamped = Math.Max(0, Math.Min(1, amount));
            var inverse = 1 - clamped;
            return Color.FromRgb(
                (byte)Math.Round((from.R * inverse) + (to.R * clamped)),
                (byte)Math.Round((from.G * inverse) + (to.G * clamped)),
                (byte)Math.Round((from.B * inverse) + (to.B * clamped)));
        }

        private static Color ToMediaColor(System.Drawing.Color color)
            => Color.FromArgb(color.A, color.R, color.G, color.B);

        private static (SolidColorBrush foreground, SolidColorBrush background) GetEditorTextBrushes(Color fallbackForeground, Color fallbackBackground)
        {
            var componentModel = Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel;
            var formatMapService = componentModel?.GetService<IClassificationFormatMapService>();
            var formatMap = formatMapService?.GetClassificationFormatMap("text");
            var defaultProperties = formatMap?.DefaultTextProperties;

            var foreground = defaultProperties?.ForegroundBrushEmpty == false ? defaultProperties.ForegroundBrush as SolidColorBrush : null;
            var background = defaultProperties?.BackgroundBrushEmpty == false ? defaultProperties.BackgroundBrush as SolidColorBrush : null;
            return (foreground ?? new SolidColorBrush(fallbackForeground), background ?? new SolidColorBrush(fallbackBackground));
        }

        private IWpfTextView? GetActiveTextView()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var textManager = Package.GetGlobalService(typeof(SVsTextManager)) as IVsTextManager;
            if (textManager is null)
            {
                return null;
            }

            textManager.GetActiveView(0, null, out var activeVsTextView);
            if (activeVsTextView is null)
            {
                return null;
            }

            var componentModel = Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel;
            var adaptersFactory = componentModel?.GetService<IVsEditorAdaptersFactoryService>();
            return adaptersFactory?.GetWpfTextView(activeVsTextView);
        }

        private static IReadOnlyList<SnapshotSpan> GetNormalizedSelectionSpans(IWpfTextView textView)
        {
            var selection = textView.Selection;

            if (selection.Mode == TextSelectionMode.Box)
            {
                return selection.SelectedSpans.ToList();
            }

            var selectionSpan = selection.StreamSelectionSpan.SnapshotSpan;
            var snapshot = selectionSpan.Snapshot;
            var firstLine = snapshot.GetLineNumberFromPosition(selectionSpan.Start);
            var lastLine = snapshot.GetLineNumberFromPosition(selectionSpan.End);
            var indentation = int.MaxValue;

            for (var lineNumber = firstLine; lineNumber <= lastLine; lineNumber++)
            {
                var (line, start, end) = GetSelectedLinePart(snapshot, lineNumber, selectionSpan);

                if (end <= start)
                {
                    continue;
                }

                var text = snapshot.GetText(start, end - start);
                var offset = text.Length - text.TrimStart().Length;

                if (offset == text.Length)
                {
                    continue;
                }

                indentation = Math.Min(indentation, start - line.Start.Position + offset);
            }

            if (indentation == int.MaxValue)
            {
                indentation = 0;
            }

            var spans = new List<SnapshotSpan>();

            for (var lineNumber = firstLine; lineNumber <= lastLine; lineNumber++)
            {
                var (line, start, end) = GetSelectedLinePart(snapshot, lineNumber, selectionSpan);
                var trimmedStart = Math.Min(Math.Max(start, line.Start.Position + indentation), end);
                spans.Add(new SnapshotSpan(snapshot, Microsoft.VisualStudio.Text.Span.FromBounds(trimmedStart, end)));
            }

            // A selection that ends at the start of a line would otherwise render a trailing empty line.
            if (spans.Count > 1 && spans[spans.Count - 1].IsEmpty)
            {
                spans.RemoveAt(spans.Count - 1);
            }

            return spans;
        }

        private static (ITextSnapshotLine line, int start, int end) GetSelectedLinePart(ITextSnapshot snapshot, int lineNumber, SnapshotSpan selectionSpan)
        {
            var line = snapshot.GetLineFromLineNumber(lineNumber);
            var start = Math.Max(line.Start.Position, selectionSpan.Start.Position);
            var end = Math.Max(start, Math.Min(line.End.Position, selectionSpan.End.Position));
            return (line, start, end);
        }

        private FlowDocument? BuildClassifiedDocument(IWpfTextView textView, IReadOnlyList<SnapshotSpan> selectedSpans)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var componentModel = Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel;
            var classifierService = componentModel?.GetService<IViewClassifierAggregatorService>();
            var formatMapService = componentModel?.GetService<IClassificationFormatMapService>();

            var classifier = classifierService?.GetClassifier(textView);
            var formatMap = formatMapService?.GetClassificationFormatMap(textView);

            if (classifier is null || formatMap is null)
            {
                return null;
            }

            var defaultProperties = formatMap.DefaultTextProperties;
            var defaultForeground = defaultProperties.ForegroundBrushEmpty ? null : defaultProperties.ForegroundBrush;
            var defaultBackground = defaultProperties.BackgroundBrushEmpty ? null : defaultProperties.BackgroundBrush;

            ApplyEditorColors(defaultForeground, defaultBackground);

            var document = CreateBaseDocument();
            var paragraph = new Paragraph { Margin = new Thickness(0) };

            for (var i = 0; i < selectedSpans.Count; i++)
            {
                AppendClassifiedSpanRuns(paragraph, classifier, formatMap, selectedSpans[i], defaultForeground, defaultBackground);
                if (i < selectedSpans.Count - 1)
                {
                    paragraph.Inlines.Add(new LineBreak());
                }
            }

            document.Blocks.Clear();
            document.Blocks.Add(paragraph);
            document.PageWidth = 100000;
            document.PagePadding = new Thickness(0);
            return document;
        }

        private void ApplyEditorColors(Brush? foreground, Brush? background)
        {
            if (foreground is not null)
            {
                PreviewRichText.Foreground = foreground;
            }

            if (background is SolidColorBrush solidBackground)
            {
                SnapshotFrame.Background = solidBackground;
                LineNumbersText.Foreground = new SolidColorBrush(Blend(
                    (foreground as SolidColorBrush)?.Color ?? Colors.Gray,
                    solidBackground.Color,
                    0.55));
            }
        }

        private static void AppendClassifiedSpanRuns(
            Paragraph paragraph,
            IClassifier classifier,
            IClassificationFormatMap formatMap,
            SnapshotSpan selectedSpan,
            Brush? defaultForeground,
            Brush? defaultBackground)
        {
            var classificationSpans = classifier.GetClassificationSpans(selectedSpan);
            var currentPosition = selectedSpan.Start;

            foreach (var classificationSpan in classificationSpans)
            {
                if (classificationSpan.Span.Start < currentPosition)
                {
                    continue;
                }

                if (classificationSpan.Span.Start > currentPosition)
                {
                    var gapSpan = new SnapshotSpan(currentPosition, classificationSpan.Span.Start);
                    AppendText(paragraph, gapSpan.GetText(), defaultForeground, null);
                }

                var textProperties = formatMap.GetTextProperties(classificationSpan.ClassificationType);
                var foreground = textProperties.ForegroundBrushEmpty ? defaultForeground : textProperties.ForegroundBrush;
                var background = textProperties.BackgroundBrushEmpty || ReferenceEquals(textProperties.BackgroundBrush, defaultBackground)
                    ? null
                    : textProperties.BackgroundBrush;

                AppendText(paragraph, classificationSpan.Span.GetText(), foreground, background);
                currentPosition = classificationSpan.Span.End;
            }

            if (currentPosition < selectedSpan.End)
            {
                var trailingSpan = new SnapshotSpan(currentPosition, selectedSpan.End);
                AppendText(paragraph, trailingSpan.GetText(), defaultForeground, null);
            }
        }

        private static void AppendText(Paragraph paragraph, string text, Brush? foreground, Brush? background)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var lines = NormalizeLineEndings(text).Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    paragraph.Inlines.Add(new LineBreak());
                }

                if (lines[i].Length == 0)
                {
                    continue;
                }

                var run = new Run(lines[i]);

                if (foreground is not null)
                {
                    run.Foreground = foreground;
                }

                if (background is not null)
                {
                    run.Background = background;
                }

                paragraph.Inlines.Add(run);
            }
        }

        private void AttachToSelectionChanges(IWpfTextView textView)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (ReferenceEquals(_trackedTextView, textView))
            {
                return;
            }

            DetachFromSelectionChanges();
            _trackedTextView = textView;
            _trackedTextView.Selection.SelectionChanged += OnTextSelectionChanged;
        }

        private void DetachFromSelectionChanges()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_trackedTextView is null)
            {
                return;
            }

            _trackedTextView.Selection.SelectionChanged -= OnTextSelectionChanged;
            _trackedTextView = null;
        }

        private void OnTextSelectionChanged(object sender, EventArgs e)
        {
            _ = RunSafeAsync(
                async () => await RefreshFromSelectionAsync(),
                "Could not refresh from the current selection.");
        }

        private void SetPreviewPlainText(string text)
        {
            var document = CreateBaseDocument();
            var paragraph = new Paragraph { Margin = new Thickness(0) };
            AppendText(paragraph, text, null, null);
            document.Blocks.Add(paragraph);
            document.PageWidth = 100000;
            PreviewRichText.Document = document;
        }

        private FlowDocument CreateBaseDocument()
            => new FlowDocument
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                PagePadding = new Thickness(0)
            };

        private async Task RunSafeAsync(Func<Task> action, string userMessage)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
                StatusText.Text = userMessage;
            }
        }
    }
}
