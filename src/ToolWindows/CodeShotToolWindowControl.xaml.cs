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
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CodeShot.ToolWindows
{
    public partial class CodeShotToolWindowControl : UserControl
    {
        // Dragging a selection raises SelectionChanged continuously, so rebuilds are debounced.
        private static readonly TimeSpan SelectionRefreshDelay = TimeSpan.FromMilliseconds(150);

        private readonly DispatcherTimer _refreshTimer;
        private string _selectedCode = string.Empty;
        private int _selectedLineCount;
        private IReadOnlyList<Inline>? _classifiedInlines;
        private IWpfTextView? _trackedTextView;
        private bool _isRefreshingSelection;
        private bool _isRefreshPending;
        private bool _isApplyingOptions;
        private bool _showLineNumbers = true;
        private bool _showTitleBar = true;
        private string _fontFamilyName = FontCatalog.FallbackFamily;
        private double _fontSize = FontCatalog.FallbackSize;

        public CodeShotToolWindowControl()
        {
            InitializeComponent();
            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = SelectionRefreshDelay
            };
            _refreshTimer.Tick += OnRefreshTimerTick;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        // The toolbar commands live in the package, so they need a way to reach the active preview.
        internal static CodeShotToolWindowControl? Current { get; private set; }

        internal bool ShowLineNumbers
        {
            get => _showLineNumbers;
            set
            {
                if (_showLineNumbers == value)
                {
                    return;
                }

                _showLineNumbers = value;
                UpdatePreviewText();
                SaveOptions();
            }
        }

        internal bool ShowTitleBar
        {
            get => _showTitleBar;
            set
            {
                if (_showTitleBar == value)
                {
                    return;
                }

                _showTitleBar = value;
                TitleBarBorder.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                SaveOptions();
            }
        }

        internal string PreviewFontFamily
        {
            get => _fontFamilyName;
            set
            {
                var resolved = FontCatalog.ResolveFamily(value);

                if (string.Equals(_fontFamilyName, resolved, StringComparison.Ordinal))
                {
                    return;
                }

                _fontFamilyName = resolved;
                ApplyFontSettings();
                SaveOptions();
            }
        }

        internal double PreviewFontSize
        {
            get => _fontSize;
            set
            {
                var clamped = FontCatalog.ClampSize(value);

                if (_fontSize == clamped)
                {
                    return;
                }

                _fontSize = clamped;
                ApplyFontSettings();
                SaveOptions();
            }
        }

        private void OnThemeChanged(ThemeChangedEventArgs e)
        {
            RunSafe(
                async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    ApplyTheme();

                    // Recoloring must not discard the preview, so the selection is only re-read
                    // when the tracked view still has one to rebuild the classified colors from.
                    if (_trackedTextView?.Selection.IsEmpty == false)
                    {
                        await RefreshFromSelectionAsync();
                    }
                },
                "Could not apply theme updates.");
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }

            VSColorTheme.ThemeChanged -= OnThemeChanged;
            General.Saved -= OnOptionsSaved;
            VS.Events.WindowEvents.ActiveFrameChanged -= OnActiveFrameChanged;
            _refreshTimer.Stop();
            DetachFromSelectionChanges();
        }

        // WPF raises Loaded and Unloaded every time the tool window is docked, floated or auto-hidden,
        // so registration has to be repeatable instead of a one-time setup.
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Current = this;

            VSColorTheme.ThemeChanged -= OnThemeChanged;
            VSColorTheme.ThemeChanged += OnThemeChanged;
            General.Saved -= OnOptionsSaved;
            General.Saved += OnOptionsSaved;
            VS.Events.WindowEvents.ActiveFrameChanged -= OnActiveFrameChanged;
            VS.Events.WindowEvents.ActiveFrameChanged += OnActiveFrameChanged;

            ApplyTheme();

            RunSafe(
                async () =>
                {
                    General options = await General.GetLiveInstanceAsync();
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    ApplyOptions(options);
                    await RefreshFromSelectionAsync();
                },
                "Could not initialize the preview.");
        }

        internal void Refresh()
        {
            RunSafe(
                RefreshFromSelectionAsync,
                "Could not refresh from the current selection.");
        }

        internal void CopyImage()
        {
            try
            {
                var snapshot = RenderSnapshot();
                if (snapshot is null)
                {
                    StatusText.Text = "Nothing to copy yet.";
                    return;
                }

                var data = new DataObject();
                data.SetImage(snapshot);

                // Copying the data keeps the image on the clipboard after Visual Studio exits.
                Clipboard.SetDataObject(data, true);
                StatusText.Text = "Copied screenshot to clipboard.";
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
                StatusText.Text = "Copy failed. Check ActivityLog for details.";
            }
        }

        internal void SaveImage()
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
                // A pass is already running and it awaits, so queue a rerun instead of
                // dropping this request and leaving the preview stale.
                _isRefreshPending = true;
                return;
            }

            _isRefreshingSelection = true;

            try
            {
                do
                {
                    _isRefreshPending = false;
                    RefreshCore();
                }
                while (_isRefreshPending);
            }
            finally
            {
                _isRefreshingSelection = false;
            }
        }

        private void RefreshCore()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

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
            _selectedLineCount = selectedSpans.Count;
            _classifiedInlines = BuildClassifiedInlines(textView, selectedSpans);

            var documentName = GetDocumentName(textView);
            TitleText.Text = string.IsNullOrWhiteSpace(documentName) ? "CodeShot" : documentName;
            StatusText.Text = _classifiedInlines is null
                ? "Preview updated from current selection (plain text fallback)."
                : "Preview updated from current selection.";

            UpdatePreviewText();
        }

        // The name comes from the captured view so it always matches the code in the preview,
        // which DTE.ActiveDocument does not guarantee and which can throw when no document is active.
        private static string GetDocumentName(IWpfTextView textView)
        {
            return EditorServices.TextDocuments?.TryGetTextDocument(textView.TextDataModel.DocumentBuffer, out var document) == true
                ? Path.GetFileName(document.FilePath)
                : string.Empty;
        }

        private void ClearSelectionPreview()
        {
            _selectedCode = string.Empty;
            _selectedLineCount = 0;
            _classifiedInlines = null;
            TitleText.Text = "No selection";
            StatusText.Text = "Select code in the editor to update the preview.";
            UpdatePreviewText();
        }

        private void UpdatePreviewText()
        {
            if (PreviewText is null || LineNumbersText is null)
            {
                return;
            }

            if (_selectedLineCount == 0)
            {
                SetPreviewPlainText(string.Empty);
                LineNumbersText.Text = string.Empty;
                ApplyFontSettings();
                return;
            }

            if (_classifiedInlines is not null)
            {
                SetPreviewInlines(_classifiedInlines);
            }
            else
            {
                SetPreviewPlainText(_selectedCode);
            }

            ApplyFontSettings();

            if (_showLineNumbers)
            {
                LineNumbersText.Text = BuildLineNumbers(_selectedLineCount);
                LineNumbersText.Visibility = Visibility.Visible;
                return;
            }

            LineNumbersText.Text = string.Empty;
            LineNumbersText.Visibility = Visibility.Collapsed;
        }

        private RenderTargetBitmap? RenderSnapshot()
        {
            // The capture surface sizes to its content rather than to the viewport, so the whole
            // selection is rendered even when the tool window is too small to show all of it.
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
            renderTarget.Freeze();
            return renderTarget;
        }

        private static string BuildLineNumbers(int lineCount)
        {
            var width = lineCount.ToString().Length;
            var builder = new StringBuilder(lineCount * (width + Environment.NewLine.Length));

            for (var number = 1; number <= lineCount; number++)
            {
                if (number > 1)
                {
                    builder.Append(Environment.NewLine);
                }

                builder.Append(number.ToString().PadLeft(width));
            }

            return builder.ToString();
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

            ApplySnapshotColors(editorForegroundBrush, editorBackgroundBrush);
        }

        // The snapshot is part of the exported image, so it is colored from the editor instead of the tool window chrome.
        private void ApplySnapshotColors(Brush? foreground, Brush? background)
        {
            var editorForeground = (foreground as SolidColorBrush)?.Color ?? Colors.Gray;
            var editorBackground = (background as SolidColorBrush)?.Color ?? Colors.White;
            var isDark = IsDark(editorBackground);

            var captureBackground = isDark
                ? Blend(editorBackground, Colors.White, 0.16)
                : Blend(editorBackground, Colors.Black, 0.12);
            var titleBarBackground = isDark
                ? Blend(editorBackground, Colors.White, 0.07)
                : Blend(editorBackground, Colors.Black, 0.05);
            var frameBorder = Blend(editorBackground, editorForeground, 0.22);

            CaptureSurface.Background = new SolidColorBrush(captureBackground);
            SnapshotFrame.Background = new SolidColorBrush(editorBackground);
            SnapshotFrame.BorderBrush = new SolidColorBrush(frameBorder);
            TitleBarBorder.Background = new SolidColorBrush(titleBarBackground);
            TitleBarBorder.BorderBrush = new SolidColorBrush(frameBorder);
            TitleBarBorder.BorderThickness = new Thickness(0, 0, 0, 1);

            PreviewText.Foreground = foreground ?? new SolidColorBrush(editorForeground);
            LineNumbersText.Foreground = new SolidColorBrush(Blend(editorForeground, editorBackground, 0.55));
            TitleText.Foreground = new SolidColorBrush(Blend(editorForeground, editorBackground, 0.15));
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
            var formatMap = EditorServices.FormatMaps?.GetClassificationFormatMap("text");
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

            return EditorServices.EditorAdapters?.GetWpfTextView(activeVsTextView);
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

                // Scanning the snapshot directly avoids allocating two strings per line just to
                // measure how far the first non-whitespace character sits from the line start.
                var contentStart = start;

                while (contentStart < end && char.IsWhiteSpace(snapshot[contentStart]))
                {
                    contentStart++;
                }

                if (contentStart == end)
                {
                    continue;
                }

                indentation = Math.Min(indentation, contentStart - line.Start.Position);
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

        private IReadOnlyList<Inline>? BuildClassifiedInlines(IWpfTextView textView, IReadOnlyList<SnapshotSpan> selectedSpans)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var classifier = EditorServices.Classifiers?.GetClassifier(textView);
            var formatMap = EditorServices.FormatMaps?.GetClassificationFormatMap(textView);

            if (classifier is null || formatMap is null)
            {
                return null;
            }

            var defaultProperties = formatMap.DefaultTextProperties;
            var defaultForeground = defaultProperties.ForegroundBrushEmpty ? null : defaultProperties.ForegroundBrush;
            var defaultBackground = defaultProperties.BackgroundBrushEmpty ? null : defaultProperties.BackgroundBrush;

            ApplyEditorColors(defaultForeground, defaultBackground);

            var inlines = new List<Inline>();

            // Classifying each line separately means one service call per line, so the whole
            // selection is classified once and the ordered result is sliced per line.
            var enclosingSpan = new SnapshotSpan(selectedSpans[0].Start, selectedSpans[selectedSpans.Count - 1].End);
            var classificationSpans = classifier.GetClassificationSpans(enclosingSpan);
            var classificationIndex = 0;

            for (var i = 0; i < selectedSpans.Count; i++)
            {
                AppendClassifiedSpanRuns(inlines, classificationSpans, ref classificationIndex, formatMap, selectedSpans[i], defaultForeground, defaultBackground);
                if (i < selectedSpans.Count - 1)
                {
                    inlines.Add(new LineBreak());
                }
            }

            return inlines;
        }

        private void ApplyEditorColors(Brush? foreground, Brush? background)
        {
            if (foreground is null && background is null)
            {
                return;
            }

            ApplySnapshotColors(foreground ?? PreviewText.Foreground, background ?? SnapshotFrame.Background);
        }

        private static void AppendClassifiedSpanRuns(
            List<Inline> inlines,
            IList<ClassificationSpan> classificationSpans,
            ref int classificationIndex,
            IClassificationFormatMap formatMap,
            SnapshotSpan selectedSpan,
            Brush? defaultForeground,
            Brush? defaultBackground)
        {
            var currentPosition = selectedSpan.Start;

            // The spans are ordered, so everything that ended before this line can be skipped for good.
            while (classificationIndex < classificationSpans.Count
                && classificationSpans[classificationIndex].Span.End <= currentPosition)
            {
                classificationIndex++;
            }

            for (var i = classificationIndex; i < classificationSpans.Count; i++)
            {
                var classificationSpan = classificationSpans[i];

                if (classificationSpan.Span.Start >= selectedSpan.End)
                {
                    break;
                }

                // A classification can start before or end after the line, for example a block comment.
                if (classificationSpan.Span.Overlap(selectedSpan) is not SnapshotSpan overlap)
                {
                    continue;
                }

                if (overlap.Start > currentPosition)
                {
                    var gapSpan = new SnapshotSpan(currentPosition, overlap.Start);
                    AppendText(inlines, gapSpan.GetText(), defaultForeground, null);
                }

                var textProperties = formatMap.GetTextProperties(classificationSpan.ClassificationType);
                var foreground = textProperties.ForegroundBrushEmpty ? defaultForeground : textProperties.ForegroundBrush;
                var background = textProperties.BackgroundBrushEmpty || ReferenceEquals(textProperties.BackgroundBrush, defaultBackground)
                    ? null
                    : textProperties.BackgroundBrush;

                AppendText(inlines, overlap.GetText(), foreground, background);
                currentPosition = overlap.End;
            }

            if (currentPosition < selectedSpan.End)
            {
                var trailingSpan = new SnapshotSpan(currentPosition, selectedSpan.End);
                AppendText(inlines, trailingSpan.GetText(), defaultForeground, null);
            }
        }

        private static void AppendText(List<Inline> inlines, string text, Brush? foreground, Brush? background)
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
                    inlines.Add(new LineBreak());
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

                inlines.Add(run);
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
            ScheduleRefresh();
        }

        // Selecting code in another document never raises SelectionChanged on the tracked view,
        // so the preview follows the active window as well.
        private void OnActiveFrameChanged(ActiveFrameChangeEventArgs e)
        {
            ScheduleRefresh();
        }

        private void ScheduleRefresh()
        {
            _refreshTimer.Stop();
            _refreshTimer.Start();
        }

        private void OnRefreshTimerTick(object sender, EventArgs e)
        {
            _refreshTimer.Stop();
            RunSafe(
                RefreshFromSelectionAsync,
                "Could not refresh from the current selection.");
        }

        private void SetPreviewPlainText(string text)
        {
            var inlines = new List<Inline>();
            AppendText(inlines, text, null, null);
            SetPreviewInlines(inlines);
        }

        private void SetPreviewInlines(IReadOnlyList<Inline> inlines)
        {
            PreviewText.Inlines.Clear();
            PreviewText.Inlines.AddRange(inlines);
        }

        private void OnOptionsSaved(General options)
        {
            RunSafe(
                async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    ApplyOptions(options);
                },
                "Could not apply the CodeShot options.");
        }

        private void ApplyOptions(General options)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var (editorFamily, editorSize) = FontCatalog.GetEditorFont();
            _isApplyingOptions = true;

            try
            {
                PreviewFontFamily = string.IsNullOrWhiteSpace(options.FontFamily) ? editorFamily : options.FontFamily;
                PreviewFontSize = options.FontSize <= 0 ? editorSize : options.FontSize;
                ShowTitleBar = options.ShowTitleBar;
                ShowLineNumbers = options.ShowLineNumbers;
            }
            finally
            {
                _isApplyingOptions = false;
            }
        }

        private void SaveOptions()
        {
            if (_isApplyingOptions)
            {
                return;
            }

            RunSafe(
                async () =>
                {
                    General options = await General.GetLiveInstanceAsync();
                    options.FontFamily = _fontFamilyName;
                    options.FontSize = _fontSize;
                    options.ShowLineNumbers = _showLineNumbers;
                    options.ShowTitleBar = _showTitleBar;
                    await options.SaveAsync();
                },
                "Could not save the CodeShot options.");
        }

        private void ApplyFontSettings()
        {
            if (PreviewText is null || LineNumbersText is null)
            {
                return;
            }

            var fontFamily = new FontFamily(_fontFamilyName);

            PreviewText.FontFamily = fontFamily;
            PreviewText.FontSize = _fontSize;
            LineNumbersText.FontFamily = fontFamily;
            LineNumbersText.FontSize = _fontSize;
        }

        // Faults are reported to the activity log instead of being left on an unobserved task.
        private void RunSafe(Func<Task> action, string userMessage)
        {
            RunSafeAsync(action, userMessage).FileAndForget($"{Vsix.Name}/{nameof(RunSafe)}");
        }

        private async Task RunSafeAsync(Func<Task> action, string userMessage)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                await ex.LogAsync();

                // The failing action can resume on a background thread, and StatusText is UI state.
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                StatusText.Text = userMessage;
            }
        }
    }
}
