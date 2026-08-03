using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CodeShot.ToolWindows
{
    public partial class CodeShotToolWindowControl : UserControl
    {
        // Dragging a selection raises SelectionChanged continuously, so rebuilds are debounced.
        private static readonly TimeSpan SelectionRefreshDelay = TimeSpan.FromMilliseconds(150);

        // Below this padding the shadow has no room to fall and is dropped instead of being clipped.
        private const int ShadowMinimumPadding = 5;

        private readonly DispatcherTimer _refreshTimer;
        private readonly HashSet<int> _highlightedLines = new HashSet<int>();
        private string _selectedCode = string.Empty;
        private int _selectedLineCount;
        private IReadOnlyList<Inline>? _classifiedInlines;
        private IWpfTextView? _trackedTextView;
        private bool _isRefreshingSelection;
        private bool _isRefreshPending;
        private bool _isApplyingOptions;
        private bool _showLineNumbers = true;
        private bool _useRealLineNumbers;
        private int _firstSelectedLineNumber = 1;
        private bool _showTitleBar = true;
        private bool _keepOriginalIndentation;
        private string _windowTitleTemplate = "{fileName}";
        private Brush _highlightBrush = Brushes.Transparent;
        private Brush _dimBrush = Brushes.Transparent;
        private string _fontFamilyName = FontCatalog.FallbackFamily;
        private double _fontSize = FontCatalog.FallbackSize;
        private double _exportScale = 2d;
        private int _padding = 10;
        private int _cornerRadius = 10;
        private bool _showShadow = true;
        private bool _isDarkBackdrop;
        private BackgroundMode _backgroundMode = BackgroundMode.Theme;
        private Color _backgroundColor = Color.FromRgb(0xAB, 0xB8, 0xC3);
        private bool _copyPlainTextWithImage;

        // The control is created and loaded asynchronously, so the request from the command is held
        // here until whichever refresh comes next has built a preview worth copying.
        private static bool _copyWhenReady;

        public CodeShotToolWindowControl(General options)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            InitializeComponent();
            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = SelectionRefreshDelay
            };
            _refreshTimer.Tick += OnRefreshTimerTick;
            CodeArea.PreviewMouseLeftButtonDown += OnCodeAreaMouseDown;
            CodeArea.SizeChanged += OnCodeAreaSizeChanged;
            ApplyOptions(options);
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

        internal bool UseRealLineNumbers
        {
            get => _useRealLineNumbers;
            set
            {
                if (_useRealLineNumbers == value)
                {
                    return;
                }

                _useRealLineNumbers = value;
                UpdatePreviewText();
                SaveOptions();
            }
        }

        internal bool KeepOriginalIndentation
        {
            get => _keepOriginalIndentation;
            set
            {
                if (_keepOriginalIndentation == value)
                {
                    return;
                }

                _keepOriginalIndentation = value;

                // The trim decides which spans are captured, so the selection has to be read again.
                Refresh();
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
            CodeArea.PreviewMouseLeftButtonDown -= OnCodeAreaMouseDown;
            CodeArea.SizeChanged -= OnCodeAreaSizeChanged;
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

            // The settings were applied when the control was created, and the applied values survive
            // being rehosted, so only the preview itself has to be rebuilt here.
            RunSafe(
                RefreshFromSelectionAsync,
                "Could not initialize the preview.");
        }

        internal void Refresh()
        {
            RunSafe(
                RefreshFromSelectionAsync,
                "Could not refresh from the current selection.");
        }

        // Invoking the command asks for a screenshot, not just for a window, so the image lands on
        // the clipboard without a second click. Selection changes deliberately do not do this,
        // because silently replacing the clipboard while the user types would be hostile.
        internal static void CopyWhenReady()
        {
            _copyWhenReady = true;
            Current?.Refresh();
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

                // Pasting an image loses the code itself, which is the long-standing complaint about
                // code screenshots. Both formats are offered so the target app can pick what it needs.
                if (_copyPlainTextWithImage && !string.IsNullOrEmpty(_selectedCode))
                {
                    data.SetText(_selectedCode);
                }

                // Copying the data keeps the image on the clipboard after Visual Studio exits.
                Clipboard.SetDataObject(data, true);
                StatusText.Text = _copyPlainTextWithImage && !string.IsNullOrEmpty(_selectedCode)
                    ? "Copied screenshot and code to clipboard."
                    : "Copied screenshot to clipboard.";
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

        private async Task RefreshFromSelectionAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (_isRefreshingSelection)
            {
                // Another call is between the thread switch and the rebuild, so queue a rerun
                // instead of dropping this request and leaving the preview stale.
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

            if (_copyWhenReady)
            {
                _copyWhenReady = false;

                // An empty preview would replace the clipboard with a blank window, so nothing is
                // copied until there is code to copy. A control that was just created has not had
                // its first layout pass either, hence waiting for layout before rendering.
                if (_selectedLineCount > 0)
                {
                    await Dispatcher.InvokeAsync(CopyImage, DispatcherPriority.Loaded);
                }
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

            var selectedSpans = GetNormalizedSelectionSpans(textView, _keepOriginalIndentation);

            if (selectedSpans.Count == 0)
            {
                ClearSelectionPreview();
                return;
            }

            _selectedCode = string.Join(Environment.NewLine, selectedSpans.Select(span => span.GetText()));
            _selectedLineCount = selectedSpans.Count;
            _firstSelectedLineNumber = selectedSpans[0].Snapshot.GetLineNumberFromPosition(selectedSpans[0].Start) + 1;
            _classifiedInlines = BuildClassifiedInlines(textView, selectedSpans);

            // The highlights are line indexes into the previous selection, so they no longer
            // point at the same code once a new selection has been read.
            _highlightedLines.Clear();

            TitleText.Text = BuildTitle(textView);
            StatusText.Text = _classifiedInlines is null
                ? "Preview updated from current selection (plain text fallback)."
                : "Preview updated from current selection.";

            UpdatePreviewText();
        }

        // The name comes from the captured view so it always matches the code in the preview,
        // which DTE.ActiveDocument does not guarantee and which can throw when no document is active.
        private static string GetDocumentPath(IWpfTextView textView)
        {
            return EditorServices.TextDocuments?.TryGetTextDocument(textView.TextDataModel.DocumentBuffer, out var document) == true
                ? document.FilePath
                : string.Empty;
        }

        private static string GetSolutionName()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (Package.GetGlobalService(typeof(SVsSolution)) is not IVsSolution solution)
            {
                return string.Empty;
            }

            solution.GetSolutionInfo(out var solutionDirectory, out var solutionFile, out _);

            // An open folder has no solution file, so the folder name stands in for the name.
            return string.IsNullOrEmpty(solutionFile)
                ? Path.GetFileName(solutionDirectory?.TrimEnd(Path.DirectorySeparatorChar) ?? string.Empty)
                : Path.GetFileNameWithoutExtension(solutionFile);
        }

        private string BuildTitle(IWpfTextView textView)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var filePath = GetDocumentPath(textView);
            var fileName = string.IsNullOrEmpty(filePath) ? string.Empty : Path.GetFileName(filePath);

            var title = _windowTitleTemplate
                .Replace("{fileName}", fileName)
                .Replace("{fileNameWithoutExtension}", string.IsNullOrEmpty(fileName) ? string.Empty : Path.GetFileNameWithoutExtension(fileName))
                .Replace("{filePath}", filePath)
                .Replace("{extension}", string.IsNullOrEmpty(fileName) ? string.Empty : Path.GetExtension(fileName).TrimStart('.'))
                .Replace("{language}", textView.TextBuffer.ContentType?.DisplayName ?? string.Empty)
                .Replace("{workspace}", GetSolutionName());

            // A template made only of tokens collapses to separators when the tokens are empty,
            // so anything that carries no text at all falls back to the extension name.
            return title.Any(char.IsLetterOrDigit) ? title.Trim() : "CodeShot";
        }

        private void ClearSelectionPreview()
        {
            _selectedCode = string.Empty;
            _selectedLineCount = 0;
            _firstSelectedLineNumber = 1;
            _classifiedInlines = null;
            _highlightedLines.Clear();
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
                LineNumbersText.Text = BuildLineNumbers(_selectedLineCount, _useRealLineNumbers ? _firstSelectedLineNumber : 1);
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
            UpdateHighlightLayers();

            if (CaptureSurface.ActualWidth <= 0 || CaptureSurface.ActualHeight <= 0)
            {
                return null;
            }

            // The export scale is used instead of the monitor DPI so the same selection produces
            // the same image on every machine, which the monitor DPI alone does not guarantee.
            var pixelWidth = Math.Max(1, (int)Math.Ceiling(CaptureSurface.ActualWidth * _exportScale));
            var pixelHeight = Math.Max(1, (int)Math.Ceiling(CaptureSurface.ActualHeight * _exportScale));

            var renderTarget = new RenderTargetBitmap(
                pixelWidth,
                pixelHeight,
                96 * _exportScale,
                96 * _exportScale,
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

        private static string BuildLineNumbers(int lineCount, int firstNumber)
        {
            var lastNumber = firstNumber + lineCount - 1;
            var width = lastNumber.ToString().Length;
            var builder = new StringBuilder(lineCount * (width + Environment.NewLine.Length));

            for (var number = firstNumber; number <= lastNumber; number++)
            {
                if (number > firstNumber)
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

            // A high contrast outline makes the window look pasted onto the backdrop, so the frame
            // is only a hairline of light or shade over the editor background the way a real window
            // edge catches the light, and the title bar is separated by its fill instead of a rule.
            var frameBorder = isDark
                ? Blend(editorBackground, Colors.White, 0.13)
                : Blend(editorBackground, Colors.Black, 0.11);
            var titleBarSeparator = isDark
                ? Blend(titleBarBackground, Colors.White, 0.05)
                : Blend(titleBarBackground, Colors.Black, 0.04);

            CaptureSurface.Background = GetCaptureBackgroundBrush(captureBackground);
            SnapshotFrame.Background = new SolidColorBrush(editorBackground);
            SnapshotFrame.BorderBrush = new SolidColorBrush(frameBorder);
            TitleBarBorder.Background = new SolidColorBrush(titleBarBackground);
            TitleBarBorder.BorderBrush = new SolidColorBrush(titleBarSeparator);
            TitleBarBorder.BorderThickness = new Thickness(0, 0, 0, 1);

            // A black shadow barely registers on a dark backdrop, so how far it is pushed depends
            // on what it falls onto.
            _isDarkBackdrop = _backgroundMode == BackgroundMode.Custom ? IsDark(_backgroundColor) : isDark;
            ApplyShadow();

            PreviewText.Foreground = foreground ?? new SolidColorBrush(editorForeground);
            LineNumbersText.Foreground = new SolidColorBrush(Blend(editorForeground, editorBackground, 0.55));
            TitleText.Foreground = new SolidColorBrush(Blend(editorForeground, editorBackground, 0.15));

            var glyphBrush = new SolidColorBrush(Blend(editorForeground, editorBackground, 0.35));
            MinimizeGlyph.Foreground = glyphBrush;
            MaximizeGlyph.Foreground = glyphBrush;
            CloseGlyph.Foreground = glyphBrush;

            // The highlight lifts a line without recoloring its syntax, and the scrim pushes the
            // rest back by fading them toward the editor background rather than toward gray.
            _highlightBrush = new SolidColorBrush(Blend(editorBackground, editorForeground, 0.14));
            _dimBrush = new SolidColorBrush(editorBackground) { Opacity = 0.62 };
            UpdateHighlightLayers();
        }

        // The overlays are sized from the rendered text, so they are rebuilt once layout has settled.
        private void OnCodeAreaSizeChanged(object sender, SizeChangedEventArgs e)
            => UpdateHighlightLayers();

        // Clicking a line is the quickest way to point at it and needs no extra toolbar UI.
        private void OnCodeAreaMouseDown(object sender, MouseButtonEventArgs e)
        {
            var lineIndex = GetLineIndexAt(e.GetPosition(PreviewText).Y);

            if (lineIndex < 0)
            {
                return;
            }

            if (_highlightedLines.Remove(lineIndex) == false)
            {
                _highlightedLines.Add(lineIndex);
            }

            UpdateHighlightLayers();
            StatusText.Text = _highlightedLines.Count == 0
                ? "Highlights cleared. Click a line to highlight it."
                : $"Highlighting {_highlightedLines.Count} line(s). Click a line to toggle it.";
            e.Handled = true;
        }

        internal bool HasHighlights => _highlightedLines.Count > 0;

        internal void ClearHighlights()
        {
            if (_highlightedLines.Count == 0)
            {
                return;
            }

            _highlightedLines.Clear();
            UpdateHighlightLayers();
            StatusText.Text = "Highlights cleared.";
        }

        // The preview is one text block of uniform monospaced lines, so the line height follows
        // from its rendered height and does not need to be measured per line.
        private double GetLineHeight()
            => _selectedLineCount <= 0 || PreviewText.ActualHeight <= 0
                ? 0
                : PreviewText.ActualHeight / _selectedLineCount;

        private int GetLineIndexAt(double y)
        {
            var lineHeight = GetLineHeight();

            if (lineHeight <= 0 || y < 0)
            {
                return -1;
            }

            var index = (int)(y / lineHeight);
            return index >= 0 && index < _selectedLineCount ? index : -1;
        }

        private void UpdateHighlightLayers()
        {
            if (HighlightLayer is null || DimLayer is null)
            {
                return;
            }

            HighlightLayer.Children.Clear();
            DimLayer.Children.Clear();

            var lineHeight = GetLineHeight();

            if (lineHeight <= 0 || _highlightedLines.Count == 0)
            {
                return;
            }

            var width = CodeArea.ActualWidth;

            for (var index = 0; index < _selectedLineCount; index++)
            {
                var isHighlighted = _highlightedLines.Contains(index);
                var layer = isHighlighted ? HighlightLayer : DimLayer;

                var rectangle = new System.Windows.Shapes.Rectangle
                {
                    Width = width,
                    Height = lineHeight,
                    Fill = isHighlighted ? _highlightBrush : _dimBrush
                };

                Canvas.SetTop(rectangle, index * lineHeight);
                layer.Children.Add(rectangle);
            }
        }

        // A transparent surface still has to be hit-testable, otherwise clicks fall through the
        // preview, so Brushes.Transparent is used instead of leaving the background unset.
        private Brush GetCaptureBackgroundBrush(Color themeBackground)
            => _backgroundMode switch
            {
                BackgroundMode.Transparent => Brushes.Transparent,
                BackgroundMode.Custom => new SolidColorBrush(_backgroundColor),
                _ => CreateBackdropBrush(themeBackground)
            };

        // A flat fill behind the window reads as an empty area rather than as a surface, so the
        // themed backdrop gets a barely visible diagonal gradient. A custom color is left exactly
        // as the user typed it, because that one is usually chosen to match something else.
        private static Brush CreateBackdropBrush(Color color)
        {
            var top = Blend(color, Colors.White, 0.07);
            var bottom = Blend(color, Colors.Black, 0.07);

            return new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0.35, 1));
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

        private static IReadOnlyList<SnapshotSpan> GetNormalizedSelectionSpans(IWpfTextView textView, bool keepOriginalIndentation)
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

            for (var lineNumber = firstLine; keepOriginalIndentation == false && lineNumber <= lastLine; lineNumber++)
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
                _exportScale = ClampExportScale(options.ExportScale);
                _copyPlainTextWithImage = options.CopyPlainTextWithImage;
                _backgroundMode = options.BackgroundMode;
                _windowTitleTemplate = string.IsNullOrWhiteSpace(options.WindowTitleTemplate) ? "{fileName}" : options.WindowTitleTemplate;
                _backgroundColor = ParseColor(options.BackgroundColor, _backgroundColor);
                ApplyWindowControls(options.WindowControls);
                ApplyShape(options.CornerRadius, options.ShowShadow);
                ApplyPadding(options.Padding);
                PreviewFontFamily = string.IsNullOrWhiteSpace(options.FontFamily) ? editorFamily : options.FontFamily;
                PreviewFontSize = options.FontSize <= 0 ? editorSize : options.FontSize;
                ShowTitleBar = options.ShowTitleBar;
                ShowLineNumbers = options.ShowLineNumbers;
                UseRealLineNumbers = options.UseRealLineNumbers;
                KeepOriginalIndentation = options.KeepOriginalIndentation;
            }
            finally
            {
                _isApplyingOptions = false;
            }

            ApplyTheme();
        }

        // An unusable value in the options page must not break the preview, so the previous color is kept.
        private static Color ParseColor(string value, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            try
            {
                return ColorConverter.ConvertFromString(value) is Color color ? color : fallback;
            }
            catch (FormatException)
            {
                return fallback;
            }
        }

        private void ApplyWindowControls(WindowControls controls)
        {
            MacWindowControls.Visibility = controls == WindowControls.MacOs ? Visibility.Visible : Visibility.Collapsed;
            WindowsWindowControls.Visibility = controls == WindowControls.Windows ? Visibility.Visible : Visibility.Collapsed;

            // The dots sit where a macOS title would push the text off center, so the title is
            // centered to match, while the other styles keep the file name against the left edge.
            TitleText.TextAlignment = controls == WindowControls.MacOs ? TextAlignment.Center : TextAlignment.Left;
        }

        private void ApplyShape(int cornerRadius, bool showShadow)
        {
            _cornerRadius = Math.Max(0, Math.Min(40, cornerRadius));
            _showShadow = showShadow;

            SnapshotFrame.CornerRadius = new CornerRadius(_cornerRadius);
            ShadowHost.CornerRadius = new CornerRadius(_cornerRadius);
            TitleBarBorder.CornerRadius = new CornerRadius(_cornerRadius, _cornerRadius, 0, 0);

            // The outer surface sits behind the frame, so it needs the larger radius of the two
            // to avoid a square corner peeking out from under a rounded one.
            CaptureSurface.CornerRadius = new CornerRadius(_cornerRadius == 0 ? 0 : _cornerRadius + 3);

            ApplyShadow();
        }

        // The shadow is drawn inside the capture surface, so without padding it would be clipped
        // away and only cost render time.
        private void ApplyShadow()
        {
            if (_showShadow == false || _padding < ShadowMinimumPadding)
            {
                ShadowHost.Effect = null;
                SnapshotFrame.Effect = null;
                return;
            }

            // Real light casts a wide ambient shadow plus a tight one where the object meets the
            // surface, and a single blur only ever looks like a smudge. Both are derived from the
            // padding, because a shadow that reaches past the edge of the image is cut off there
            // and leaves a hard line exactly where the falloff should be softest.
            var ambientBlur = Math.Min(64, _padding * 0.9);
            var ambientDepth = _padding * 0.22;
            var contactBlur = _padding * 0.3;
            var contactDepth = _padding * 0.1;

            ShadowHost.Effect = new DropShadowEffect
            {
                BlurRadius = ambientBlur,
                ShadowDepth = ambientDepth,
                Direction = 270,
                Opacity = _isDarkBackdrop ? 0.42 : 0.26,
                Color = Colors.Black
            };

            SnapshotFrame.Effect = new DropShadowEffect
            {
                BlurRadius = contactBlur,
                ShadowDepth = contactDepth,
                Direction = 270,
                Opacity = _isDarkBackdrop ? 0.3 : 0.18,
                Color = Colors.Black
            };
        }

        private void ApplyPadding(int padding)
        {
            _padding = Math.Max(0, Math.Min(200, padding));
            CaptureSurface.Padding = new Thickness(_padding);
            ApplyShadow();
        }

        private static double ClampExportScale(double value)
            => value <= 0 ? 1d : Math.Min(8d, value);

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
                    options.UseRealLineNumbers = _useRealLineNumbers;
                    options.KeepOriginalIndentation = _keepOriginalIndentation;
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

            // The default line spacing of a monospaced font is tight enough that a screenshot reads
            // as a wall of text, so the lines are opened up the way a code sample in an article is.
            var lineHeight = Math.Round(_fontSize * 1.45);

            PreviewText.FontFamily = fontFamily;
            PreviewText.FontSize = _fontSize;
            PreviewText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            PreviewText.LineHeight = lineHeight;
            LineNumbersText.FontFamily = fontFamily;
            LineNumbersText.FontSize = _fontSize;
            LineNumbersText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            LineNumbersText.LineHeight = lineHeight;
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
