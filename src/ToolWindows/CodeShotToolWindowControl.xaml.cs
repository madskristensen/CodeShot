using EnvDTE;
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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        private const double CropEdgeSnapDistance = 12;
        private const double ZoomStep = 10;
        private const int MaximumRenderDimension = 16384;
        private const long MaximumRenderPixelCount = 64_000_000;

        private readonly DispatcherTimer _refreshTimer;
        private readonly DispatcherTimer _menuCaptureCountdownTimer;
        private readonly HashSet<int> _highlightedLines = new HashSet<int>();
        private readonly BoundedEditHistory<PreviewEditState> _editHistory = new BoundedEditHistory<PreviewEditState>(100);
        private readonly AnnotationController _annotationController;
        private string _selectedCode = string.Empty;
        private int _selectedLineCount;
        private ToolWindowSnapshot? _capturedToolWindow;
        private bool _isCropping;
        private Point? _cropStart;
        private System.Windows.Shapes.Rectangle? _cropSelection;
        private IReadOnlyList<IReadOnlyList<Inline>>? _classifiedLines;
        private readonly List<FrameworkElement> _previewLineRows = new List<FrameworkElement>();
        private readonly List<Paragraph> _previewParagraphs = new List<Paragraph>();
        private readonly List<TextBlock> _previewTextBlocks = new List<TextBlock>();
        private readonly List<TextBlock> _lineNumberTextBlocks = new List<TextBlock>();
        private WeakReference<IWpfTextView>? _previewTextView;
        private PreviewSpanIdentity[] _previewSpans = Array.Empty<PreviewSpanIdentity>();
        private IWpfTextView? _trackedTextView;
        private bool _isRefreshingSelection;
        private bool _isRefreshPending;
        private bool _isApplyingOptions;
        private bool _isTextWidthCustomized;
        private bool _showLineNumbers = true;
        private bool _useRealLineNumbers;
        private int _firstSelectedLineNumber = 1;
        private bool _showTitleBar = true;
        private bool _keepOriginalIndentation;
        private string _windowTitleTemplate = "{fileName}";
        private DocumentTokens _documentTokens = DocumentTokens.Empty;
        private Brush _highlightBrush = Brushes.Transparent;
        private Brush _dimBrush = Brushes.Transparent;
        private Brush _previewForeground = Brushes.Gray;
        private Brush _lineNumberForeground = Brushes.Gray;
        private string _fontFamilyName = FontCatalog.FallbackFamily;
        private double _fontSize = FontCatalog.FallbackSize;
        private double _lineHeightMultiplier = 1.45d;
        private double _exportScale = 2d;
        private int _padding = 10;
        private int _cornerRadius = 10;
        private bool _showShadow = true;
        private bool _isDarkBackdrop;
        private BackgroundMode _backgroundMode = BackgroundMode.Theme;
        private Color _backgroundColor = Color.FromRgb(0xAB, 0xB8, 0xC3);
        private Color _gradientStartColor = Color.FromRgb(0x6B, 0xCB, 0xA5);
        private Color _gradientEndColor = Color.FromRgb(0xCA, 0xF4, 0xC2);
        private int _gradientAngle = 135;
        private bool _copyPlainTextWithImage;
        private string _saveFolder = string.Empty;
        private string _saveFileNameTemplate = "{fileNameWithoutExtension}";
        private bool _promptForSaveLocation = true;
        private string _lastRenderFailure = string.Empty;
        private string? _statusBeforeMenuCapture;
        private string? _statusBeforeLoading;
        private int _menuCaptureRemainingSeconds;

        // The control is created and loaded asynchronously, so requests from commands are held until
        // the control exists and the preview has finished rendering.
        private static bool _copyWhenReady;
        private static string? _pendingLoadingMessage;
        private static ToolWindowSnapshot? _pendingToolWindowSnapshot;

        public CodeShotToolWindowControl(General options)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            InitializeComponent();
            _annotationController = new AnnotationController(CodeArea, AnnotationLayer, message => StatusText.Text = message);
            _annotationController.Changing += OnAnnotationsChanging;
            _annotationController.HistoryInvalidated += ClearEditHistory;
            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = SelectionRefreshDelay
            };
            _refreshTimer.Tick += OnRefreshTimerTick;
            _menuCaptureCountdownTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _menuCaptureCountdownTimer.Tick += OnMenuCaptureCountdownTick;
            ApplyOptions(options);
            ShowEmptyState(HasOpenDocument());
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        // The toolbar commands live in the package, so they need a way to reach the active preview.
        internal static CodeShotToolWindowControl? Current { get; private set; }
        internal bool SupportsCodeFormatting => _capturedToolWindow is null;

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
                ThreadHelper.ThrowIfNotOnUIThread();

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
                TitleBarBorder.Visibility = value && _capturedToolWindow is null
                    ? Visibility.Visible
                    : Visibility.Collapsed;
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
            _menuCaptureCountdownTimer.Stop();
            MenuCaptureCountdown.Visibility = Visibility.Collapsed;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            _statusBeforeMenuCapture = null;
            _statusBeforeLoading = null;
            CodeArea.PreviewMouseLeftButtonDown -= OnCodeAreaMouseDown;
            CodeArea.PreviewMouseMove -= OnCodeAreaMouseMove;
            CodeArea.PreviewMouseLeftButtonUp -= OnCodeAreaMouseUp;
            CodeArea.SizeChanged -= OnCodeAreaSizeChanged;
            CaptureSurface.SizeChanged -= OnCaptureSurfaceSizeChanged;
            DetachFromSelectionChanges();
        }

        // WPF raises Loaded and Unloaded every time the tool window is docked, floated or auto-hidden,
        // so registration has to be repeatable instead of a one-time setup.
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Current = this;

            CodeArea.PreviewMouseLeftButtonDown -= OnCodeAreaMouseDown;
            CodeArea.PreviewMouseLeftButtonDown += OnCodeAreaMouseDown;
            CodeArea.PreviewMouseMove -= OnCodeAreaMouseMove;
            CodeArea.PreviewMouseMove += OnCodeAreaMouseMove;
            CodeArea.PreviewMouseLeftButtonUp -= OnCodeAreaMouseUp;
            CodeArea.PreviewMouseLeftButtonUp += OnCodeAreaMouseUp;
            CodeArea.SizeChanged -= OnCodeAreaSizeChanged;
            CodeArea.SizeChanged += OnCodeAreaSizeChanged;
            CaptureSurface.SizeChanged -= OnCaptureSurfaceSizeChanged;
            CaptureSurface.SizeChanged += OnCaptureSurfaceSizeChanged;
            VSColorTheme.ThemeChanged -= OnThemeChanged;
            VSColorTheme.ThemeChanged += OnThemeChanged;
            General.Saved -= OnOptionsSaved;
            General.Saved += OnOptionsSaved;
            VS.Events.WindowEvents.ActiveFrameChanged -= OnActiveFrameChanged;
            VS.Events.WindowEvents.ActiveFrameChanged += OnActiveFrameChanged;

            ApplyTheme();

            if (_pendingLoadingMessage is string loadingMessage)
            {
                ShowLoadingCore(loadingMessage);
            }

            if (_pendingToolWindowSnapshot is ToolWindowSnapshot pendingSnapshot)
            {
                _pendingToolWindowSnapshot = null;
                ShowCapturedImage(pendingSnapshot);
                return;
            }

            // Rehosting must not replace an image that is currently being annotated.
            if (_capturedToolWindow is not null)
            {
                return;
            }

            // The settings were applied when the control was created, and the applied values survive
            // being rehosted, so only the preview itself has to be rebuilt here.
            RunSafe(
                RefreshFromSelectionAsync,
                "Could not initialize the preview.");
        }

        internal void Refresh()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ExitCapturedImageMode();
            ResetTextWidth();
            RunSafe(
                RefreshFromSelectionAsync,
                "Could not refresh from the current selection.");
        }

        // Invoking the command asks for a screenshot, not just for a window, so the image lands on
        // the clipboard without a second click. Selection changes deliberately do not do this,
        // because silently replacing the clipboard while the user types would be hostile.
        internal static void CopyWhenReady()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ShowLoading("Creating editor screenshot...");
            _copyWhenReady = true;
            Current?.Refresh();
        }

        internal static void ShowCapturedImageWhenReady(ToolWindowSnapshot snapshot)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _pendingToolWindowSnapshot = snapshot;

            if (Current is CodeShotToolWindowControl control)
            {
                _pendingToolWindowSnapshot = null;
                control.ShowCapturedImage(snapshot);
            }
        }

        internal static void ShowLoading(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _pendingLoadingMessage = message;
            Current?.ShowLoadingCore(message);
        }

        internal static void HideLoading()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _pendingLoadingMessage = null;
            Current?.HideLoadingCore();
        }

        private void ShowLoadingCore(string message)
        {
            _statusBeforeLoading ??= StatusText.Text;
            LoadingText.Text = message;
            LoadingOverlay.Visibility = Visibility.Visible;
            StatusText.Text = message;
        }

        private void HideLoadingCore()
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            if (_statusBeforeLoading is string status)
            {
                StatusText.Text = status;
                _statusBeforeLoading = null;
            }
        }

        internal static void StartMenuCaptureCountdown(int seconds)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (Current is not CodeShotToolWindowControl control)
            {
                return;
            }

            control._statusBeforeMenuCapture ??= control.StatusText.Text;
            control._menuCaptureRemainingSeconds = seconds;
            control.UpdateMenuCaptureCountdown();
            control._menuCaptureCountdownTimer.Start();
        }

        private void OnMenuCaptureCountdownTick(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _menuCaptureRemainingSeconds--;
            if (_menuCaptureRemainingSeconds <= 0)
            {
                _menuCaptureCountdownTimer.Stop();
                ShowLoading("Creating foreground UI screenshot...");
                MenuCaptureCountdown.Visibility = Visibility.Collapsed;
                return;
            }

            UpdateMenuCaptureCountdown();
        }

        private void UpdateMenuCaptureCountdown()
        {
            MenuCaptureCountdownText.Text = _menuCaptureRemainingSeconds.ToString();
            MenuCaptureCountdown.Visibility = Visibility.Visible;
            StatusText.Text = $"Capturing foreground Visual Studio UI in {_menuCaptureRemainingSeconds}...";
        }

        internal static void HideMenuCaptureCountdown()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (Current is not CodeShotToolWindowControl control)
            {
                return;
            }

            control._menuCaptureCountdownTimer.Stop();
            control.MenuCaptureCountdown.Visibility = Visibility.Collapsed;
            if (control._statusBeforeMenuCapture is string status)
            {
                control.StatusText.Text = status;
                control._statusBeforeMenuCapture = null;
            }
        }

        private void ShowCapturedImage(
            ToolWindowSnapshot snapshot,
            bool resetEditHistory = true,
            AnnotationController.State? annotationState = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            CancelCrop();

            if (resetEditHistory)
            {
                ClearEditHistory();
            }

            _refreshTimer.Stop();
            DetachFromSelectionChanges();
            _capturedToolWindow = snapshot;
            _selectedCode = string.Empty;
            _selectedLineCount = 0;
            _classifiedLines = null;
            _previewTextView = null;
            _previewSpans = Array.Empty<PreviewSpanIdentity>();
            _highlightedLines.Clear();
            _annotationController.Reset();
            _documentTokens = DocumentTokens.Empty;

            HideEmptyState();
            ClearPreviewLines();
            PreviewLinesPanel.Visibility = Visibility.Collapsed;
            TextWidthThumb.Visibility = Visibility.Collapsed;
            HighlightLayer.Visibility = Visibility.Collapsed;
            DimLayer.Visibility = Visibility.Collapsed;
            CapturedImage.Source = snapshot.Image;
            CapturedImage.Width = snapshot.Image.PixelWidth;
            CapturedImage.Height = snapshot.Image.PixelHeight;
            CapturedImage.Visibility = Visibility.Visible;
            CodeArea.Width = snapshot.Image.PixelWidth;
            CodeArea.Height = snapshot.Image.PixelHeight;
            CodeArea.Margin = new Thickness(0);
            SnapshotFrame.MinWidth = 0;
            SnapshotFrame.Background = Brushes.Transparent;
            SnapshotFrame.BorderThickness = new Thickness(0);
            TitleBarBorder.Visibility = Visibility.Collapsed;
            ApplyShape(_cornerRadius, _showShadow);
            ApplyPadding(_padding);
            ApplyTheme();
            CodeArea.UpdateLayout();
            UpdateScreenshotDimensions();

            if (annotationState is not null)
            {
                _annotationController.RestoreState(annotationState);
            }

            HideLoading();
            StatusText.Text = $"Captured '{snapshot.Caption}' and copied it to the clipboard. Add annotations, then copy or save again.";
            UpdateCommandStatus();
        }

        private void ExitCapturedImageMode()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_capturedToolWindow is null)
            {
                return;
            }

            CancelCrop();
            ClearEditHistory();
            _capturedToolWindow = null;
            _annotationController.Reset();
            CapturedImage.Source = null;
            CapturedImage.Visibility = Visibility.Collapsed;
            CapturedImage.ClearValue(WidthProperty);
            CapturedImage.ClearValue(HeightProperty);
            CodeArea.ClearValue(WidthProperty);
            CodeArea.ClearValue(HeightProperty);
            CodeArea.Margin = new Thickness(16, 0, 16, 16);
            SnapshotFrame.MinWidth = TextCaptureWidth.Minimum;
            SnapshotFrame.BorderThickness = new Thickness(1);
            PreviewLinesPanel.Visibility = Visibility.Visible;
            TextWidthThumb.Visibility = Visibility.Visible;
            HighlightLayer.Visibility = Visibility.Visible;
            DimLayer.Visibility = Visibility.Visible;
            TitleBarBorder.Visibility = _showTitleBar ? Visibility.Visible : Visibility.Collapsed;
            ApplyShape(_cornerRadius, _showShadow);
            ApplyPadding(_padding);
            ApplyTheme();
        }

        internal static async Task CopyImageToClipboardAsync(
            BitmapSource snapshot,
            string? plainText = null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (snapshot.IsFrozen == false)
            {
                snapshot.Freeze();
            }

            using (var png = await Task.Run(() => PngImageEncoder.Encode(snapshot)))
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var data = new DataObject();
                data.SetImage(snapshot);

                if (string.IsNullOrEmpty(plainText) == false)
                {
                    data.SetText(plainText);
                }

                // WPF's standard bitmap clipboard format drops alpha in many paste targets. The PNG
                // format preserves transparency while SetImage remains as a compatibility fallback.
                data.SetData("PNG", png, false);

                // Copying the data keeps the image on the clipboard after Visual Studio exits.
                Clipboard.SetDataObject(data, true);
            }
        }

        internal async Task CopyImageAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                if (_annotationController.CopyText())
                {
                    return;
                }

                CancelCrop();
                var snapshot = RenderSnapshot();
                if (snapshot is null)
                {
                    StatusText.Text = GetRenderFailureMessage("Nothing to copy yet.");
                    return;
                }

                // Pasting an image loses the code itself, which is the long-standing complaint about
                // code screenshots. A redaction must also suppress the original text, otherwise an
                // app that prefers text could receive the sensitive value hidden in the image.
                var includePlainText = _copyPlainTextWithImage
                    && _annotationController.HasRedactions == false
                    && !string.IsNullOrEmpty(_selectedCode);
                await CopyImageToClipboardAsync(
                    snapshot,
                    includePlainText ? _selectedCode : null);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                StatusText.Text = includePlainText
                    ? "Copied screenshot and code to clipboard."
                    : _copyPlainTextWithImage && _annotationController.HasRedactions
                        ? "Copied screenshot. Plain text was omitted because the image contains a redaction."
                        : "Copied screenshot to clipboard.";
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                StatusText.Text = "Copy failed. Check ActivityLog for details.";
            }
        }

        internal void SaveImage()
        {
            try
            {
                CancelCrop();
                var snapshot = RenderSnapshot();
                if (snapshot is null)
                {
                    StatusText.Text = GetRenderFailureMessage("Nothing to save yet.");
                    return;
                }

                var fileName = _documentTokens.ExpandFileName(_saveFileNameTemplate, "codeshot");
                var folder = GetSaveFolder();

                if (TryGetSavePath(fileName, folder, out var path, out var overwrite) == false)
                {
                    return;
                }

                path = ScreenshotFileStore.Save(snapshot, path, overwrite);

                // The folder is remembered so that the next save starts where the last one landed,
                // which is what the dialog would have done on its own before it could be skipped.
                RememberSaveFolder(Path.GetDirectoryName(path));
                StatusText.Text = $"Saved screenshot to '{path}'.";
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
                StatusText.Text = "Save failed. Check ActivityLog for details.";
            }
        }

        private string GetSaveFolder()
        {
            if (string.IsNullOrWhiteSpace(_saveFolder))
            {
                return string.Empty;
            }

            try
            {
                return Directory.Exists(_saveFolder) ? _saveFolder : string.Empty;
            }
            catch (Exception ex)
            {
                // A path that cannot even be tested, such as one on a disconnected share, must not
                // stop the save, so the dialog takes over from here.
                _ = ex.LogAsync();
                return string.Empty;
            }
        }

        private bool TryGetSavePath(string fileName, string folder, out string path, out bool overwrite)
        {
            // Saving without asking needs somewhere to put the file, so a missing or unusable folder
            // falls back to the dialog rather than dropping the image somewhere unexpected.
            if (_promptForSaveLocation == false && folder.Length > 0)
            {
                path = Path.Combine(folder, fileName + ".png");
                overwrite = false;
                return true;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "PNG image (*.png)|*.png",
                AddExtension = true,
                DefaultExt = ".png",
                FileName = fileName + ".png"
            };

            if (folder.Length > 0)
            {
                dialog.InitialDirectory = folder;
            }

            if (dialog.ShowDialog() != true)
            {
                path = string.Empty;
                overwrite = false;
                return false;
            }

            path = dialog.FileName;
            overwrite = true;
            return true;
        }

        private void RememberSaveFolder(string? folder)
        {
            if (string.IsNullOrEmpty(folder) || string.Equals(_saveFolder, folder, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _saveFolder = folder!;
            SaveOptions();
        }

        private async Task RefreshFromSelectionAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (_copyWhenReady)
            {
                await Dispatcher.Yield(DispatcherPriority.Render);
            }

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
                UpdateCommandStatus();
            }

            if (_copyWhenReady)
            {
                _copyWhenReady = false;

                // An empty preview would replace the clipboard with a blank window, so nothing is
                // copied until there is code to copy. A control that was just created has not had
                // its first layout pass either, hence waiting for layout before rendering.
                if (_selectedLineCount > 0)
                {
                    await Dispatcher.Yield(DispatcherPriority.Loaded);
                    await CopyImageAsync();
                }

                HideLoading();
            }
        }

        private static void UpdateCommandStatus()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (Package.GetGlobalService(typeof(SVsUIShell)) is IVsUIShell uiShell)
            {
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(uiShell.UpdateCommandUI(0));
            }
        }

        private void RefreshCore()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var textView = GetActiveTextView();

            // Activating the CodeShot tool window leaves no active editor view. Keep the captured
            // selection in that case so annotations survive while the user works in the preview.
            if (textView is null)
            {
                DetachFromSelectionChanges();

                if (HasPreview == false)
                {
                    ShowEmptyState(HasOpenDocument());
                }

                return;
            }

            // Track empty views too, otherwise selecting text later in the same editor never schedules
            // a refresh and the previous editor remains strongly referenced.
            AttachToSelectionChanges(textView);

            if (textView.Selection.IsEmpty || textView.Selection.SelectedSpans.Count == 0)
            {
                ClearSelectionPreview();
                return;
            }

            var selectedSpans = GetNormalizedSelectionSpans(textView, _keepOriginalIndentation);

            if (selectedSpans.Count == 0)
            {
                ClearSelectionPreview();
                return;
            }

            var selectionChanged = IsPreviewSelectionChanged(textView, selectedSpans);
            _selectedCode = string.Join(Environment.NewLine, selectedSpans.Select(span => span.GetText()));
            _selectedLineCount = selectedSpans.Count;
            _firstSelectedLineNumber = selectedSpans[0].Snapshot.GetLineNumberFromPosition(selectedSpans[0].Start) + 1;
            _classifiedLines = BuildClassifiedLines(textView, selectedSpans);
            _previewTextView = new WeakReference<IWpfTextView>(textView);
            _previewSpans = CapturePreviewSpanIdentities(selectedSpans);

            // The highlights are line indexes into the previous selection, so they no longer
            // point at the same code once a new selection has been read.
            if (selectionChanged)
            {
                ResetTextWidth();
                _highlightedLines.Clear();
                ClearEditHistory();
                _annotationController.Reset();
            }

            _documentTokens = CaptureDocumentTokens(textView);
            TitleText.Text = _documentTokens.ExpandOrDefault(_windowTitleTemplate, "CodeShot");
            StatusText.Text = _classifiedLines is null
                ? "Preview updated from current selection (plain text fallback)."
                : "Preview updated from current selection.";

            HideEmptyState();
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

        private static DocumentTokens CaptureDocumentTokens(IWpfTextView textView)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            return new DocumentTokens(
                GetDocumentPath(textView),
                textView.TextBuffer.ContentType?.DisplayName,
                GetSolutionName());
        }

        private void ClearSelectionPreview()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _selectedCode = string.Empty;
            _selectedLineCount = 0;
            _firstSelectedLineNumber = 1;
            _classifiedLines = null;
            _previewTextView = null;
            _previewSpans = Array.Empty<PreviewSpanIdentity>();
            _highlightedLines.Clear();
            ClearEditHistory();
            _annotationController.Reset();
            _documentTokens = DocumentTokens.Empty;
            TitleText.Text = "No selection";
            ShowEmptyState(hasActiveDocument: true);
            UpdatePreviewText();
            UpdateScreenshotDimensions();
        }

        private void ShowEmptyState(bool hasActiveDocument)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var description = hasActiveDocument
                ? "Select code in the editor and the preview will appear here."
                : "Open a file and select the code you want to capture.";
            var shortcut = GetTakeScreenshotShortcut();

            EmptyStateDescription.Text = description;
            EmptyStateShortcut.Text = string.IsNullOrWhiteSpace(shortcut)
                ? "After selecting code, choose Tools > Take Screenshot to copy it immediately."
                : $"After selecting code, press {shortcut} to copy it immediately.";
            EmptyState.Visibility = Visibility.Visible;
            CaptureSurface.Visibility = Visibility.Collapsed;
            TextWidthThumb.Visibility = Visibility.Collapsed;
            StatusText.Text = description;
        }

        private void HideEmptyState()
        {
            EmptyState.Visibility = Visibility.Collapsed;
            CaptureSurface.Visibility = Visibility.Visible;
            TextWidthThumb.Visibility = _capturedToolWindow is null ? Visibility.Visible : Visibility.Collapsed;
        }

        private static bool HasOpenDocument()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (Package.GetGlobalService(typeof(SDTE)) is not DTE dte)
            {
                return false;
            }

            try
            {
                return dte.ActiveDocument is not null;
            }
            catch (COMException ex)
            {
                _ = ex.LogAsync();
                return false;
            }
        }

        private static string? GetTakeScreenshotShortcut()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (Package.GetGlobalService(typeof(SDTE)) is not DTE dte)
            {
                return null;
            }

            try
            {
                Command command = dte.Commands.Item(
                    PackageGuids.CodeShot.ToString("B"),
                    PackageIds.ShowCodeShotWindowCommand);

                if (command?.Bindings is not object[] bindings)
                {
                    return null;
                }

                foreach (var binding in bindings.OfType<string>())
                {
                    var separator = binding.IndexOf("::", StringComparison.Ordinal);
                    var shortcut = separator >= 0 ? binding.Substring(separator + 2) : binding;

                    if (string.IsNullOrWhiteSpace(shortcut) == false)
                    {
                        return shortcut;
                    }
                }
            }
            catch (ArgumentException ex)
            {
                _ = ex.LogAsync();
            }
            catch (COMException ex)
            {
                _ = ex.LogAsync();
            }

            return null;
        }

        private bool IsPreviewSelectionChanged(IWpfTextView textView, IReadOnlyList<SnapshotSpan> spans)
        {
            if (_previewTextView is null
                || _previewTextView.TryGetTarget(out var previousTextView) == false
                || ReferenceEquals(previousTextView, textView) == false
                || _previewSpans.Length != spans.Count)
            {
                return true;
            }

            for (var index = 0; index < spans.Count; index++)
            {
                if (_previewSpans[index].Matches(spans[index]) == false)
                {
                    return true;
                }
            }

            return false;
        }

        private static PreviewSpanIdentity[] CapturePreviewSpanIdentities(IReadOnlyList<SnapshotSpan> spans)
        {
            var identities = new PreviewSpanIdentity[spans.Count];

            for (var index = 0; index < spans.Count; index++)
            {
                identities[index] = new PreviewSpanIdentity(spans[index]);
            }

            return identities;
        }

        private void UpdatePreviewText()
        {
            if (PreviewLinesPanel is null)
            {
                return;
            }

            ClearPreviewLines();

            if (_selectedLineCount == 0)
            {
                return;
            }

            var plainTextLines = NormalizeLineEndings(_selectedCode).Split('\n');
            var firstLineNumber = _useRealLineNumbers ? _firstSelectedLineNumber : 1;
            var lineNumberWidth = (firstLineNumber + _selectedLineCount - 1).ToString().Length;

            for (var index = 0; index < _selectedLineCount; index++)
            {
                var lineText = index < plainTextLines.Length ? plainTextLines[index] : string.Empty;
                var inlines = _classifiedLines is null
                    ? CreatePlainTextInlines(lineText)
                    : _classifiedLines[index];
                AddPreviewLine(inlines, lineText, firstLineNumber + index, lineNumberWidth);
            }

            ApplyFontSettings();
            ApplyPreviewTextColors();
        }

        private void AddPreviewLine(IReadOnlyList<Inline> inlines, string lineText, int lineNumber, int lineNumberWidth)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_showLineNumbers ? 16 : 0) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lineNumberText = new TextBlock
            {
                Text = _showLineNumbers ? lineNumber.ToString().PadLeft(lineNumberWidth) : string.Empty,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Visibility = _showLineNumbers ? Visibility.Visible : Visibility.Collapsed
            };
            FrameworkElement previewText;

            if (_isTextWidthCustomized)
            {
                var paragraph = new Paragraph
                {
                    Margin = new Thickness(0),
                    Tag = TextWrapIndent.Split(lineText).Whitespace + "  "
                };
                paragraph.Inlines.AddRange(inlines);
                var document = new FlowDocument(paragraph)
                {
                    ColumnGap = 0,
                    PagePadding = new Thickness(0)
                };
                previewText = new RichTextBox(document)
                {
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Focusable = false,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    IsDocumentEnabled = false,
                    IsReadOnly = true,
                    Padding = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Top,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
                };
                _previewParagraphs.Add(paragraph);
            }
            else
            {
                var textBlock = new TextBlock
                {
                    TextWrapping = TextWrapping.NoWrap,
                    VerticalAlignment = VerticalAlignment.Top
                };
                textBlock.Inlines.AddRange(inlines);
                previewText = textBlock;
                _previewTextBlocks.Add(textBlock);
            }

            Grid.SetColumn(lineNumberText, 0);
            Grid.SetColumn(previewText, 2);
            row.Children.Add(lineNumberText);
            row.Children.Add(previewText);
            PreviewLinesPanel.Children.Add(row);
            _previewLineRows.Add(row);
            _lineNumberTextBlocks.Add(lineNumberText);
        }

        private static IReadOnlyList<Inline> CreatePlainTextInlines(string text)
        {
            var inlines = new List<Inline>();
            AppendText(inlines, text, null, null);
            return inlines;
        }

        private void ClearPreviewLines()
        {
            // Classified inlines are reused when display options change, so detach them from their
            // current TextBlocks before constructing replacement rows.
            foreach (var paragraph in _previewParagraphs)
            {
                paragraph.Inlines.Clear();
            }

            foreach (var textBlock in _previewTextBlocks)
            {
                textBlock.Inlines.Clear();
            }

            PreviewLinesPanel.Children.Clear();
            _previewLineRows.Clear();
            _previewParagraphs.Clear();
            _previewTextBlocks.Clear();
            _lineNumberTextBlocks.Clear();
        }

        private RenderTargetBitmap? RenderSnapshot(double? requestedScale = null)
        {
            _lastRenderFailure = string.Empty;
            _annotationController.CommitTextEdit();

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
            var scale = requestedScale ?? _exportScale;
            var pixelWidthValue = Math.Ceiling(CaptureSurface.ActualWidth * scale);
            var pixelHeightValue = Math.Ceiling(CaptureSurface.ActualHeight * scale);

            if (double.IsNaN(scale)
                || double.IsInfinity(scale)
                || scale <= 0
                || pixelWidthValue > MaximumRenderDimension
                || pixelHeightValue > MaximumRenderDimension
                || pixelWidthValue * pixelHeightValue > MaximumRenderPixelCount)
            {
                _lastRenderFailure = "The screenshot is too large to render safely. Reduce the selection or export scale.";
                return null;
            }

            _annotationController.SetSelectionAdornersVisible(false);

            try
            {
                var pixelWidth = Math.Max(1, (int)pixelWidthValue);
                var pixelHeight = Math.Max(1, (int)pixelHeightValue);
                var renderTarget = new RenderTargetBitmap(
                    pixelWidth,
                    pixelHeight,
                    96 * scale,
                    96 * scale,
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
            catch (Exception ex)
            {
                _ = ex.LogAsync();
                _lastRenderFailure = "The screenshot could not be rendered within available memory.";
                return null;
            }
            finally
            {
                _annotationController.SetSelectionAdornersVisible(true);
            }
        }

        private string GetRenderFailureMessage(string fallback)
            => string.IsNullOrEmpty(_lastRenderFailure) ? fallback : _lastRenderFailure;

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

            CaptureSurface.Background = _capturedToolWindow is null
                ? GetCaptureBackgroundBrush(captureBackground)
                : Brushes.Transparent;
            SnapshotFrame.Background = _capturedToolWindow is null
                ? new SolidColorBrush(editorBackground)
                : Brushes.Transparent;
            SnapshotFrame.BorderBrush = new SolidColorBrush(frameBorder);
            TitleBarBorder.Background = new SolidColorBrush(titleBarBackground);
            TitleBarBorder.BorderBrush = new SolidColorBrush(titleBarSeparator);
            TitleBarBorder.BorderThickness = new Thickness(0, 0, 0, 1);

            // A black shadow barely registers on a dark backdrop, so how far it is pushed depends
            // on what it falls onto.
            _isDarkBackdrop = _backgroundMode switch
            {
                BackgroundMode.Custom => IsDark(_backgroundColor),
                BackgroundMode.Gradient => IsDark(Blend(_gradientStartColor, _gradientEndColor, 0.5)),
                _ => isDark
            };
            ApplyShadow();

            _previewForeground = foreground ?? new SolidColorBrush(editorForeground);
            _lineNumberForeground = new SolidColorBrush(Blend(editorForeground, editorBackground, 0.55));
            ApplyPreviewTextColors();
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
        {
            UpdateHighlightLayers();
            _annotationController.HandleSurfaceSizeChanged();
        }

        private void OnCaptureSurfaceSizeChanged(object sender, SizeChangedEventArgs e)
            => UpdateScreenshotDimensions();

        private void UpdateScreenshotDimensions()
        {
            if (HasPreview == false || CaptureSurface.ActualWidth <= 0 || CaptureSurface.ActualHeight <= 0)
            {
                DimensionsText.Text = string.Empty;
                return;
            }

            var width = Math.Max(1, (int)Math.Ceiling(CaptureSurface.ActualWidth));
            var height = Math.Max(1, (int)Math.Ceiling(CaptureSurface.ActualHeight));
            DimensionsText.Text = $"{width} x {height} px";
        }

        private void OnZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var scale = e.NewValue / 100;
            ZoomTransform.ScaleX = scale;
            ZoomTransform.ScaleY = scale;
        }

        private void OnTextWidthDragStarted(object sender, DragStartedEventArgs e)
        {
            if (_selectedLineCount == 0 || _capturedToolWindow is not null)
            {
                return;
            }

            var currentWidth = TextCaptureWidth.Clamp(SnapshotFrame.ActualWidth);
            SnapshotFrame.Width = currentWidth;
            _isTextWidthCustomized = true;
            UpdatePreviewText();
        }

        private void OnTextWidthDragDelta(object sender, DragDeltaEventArgs e)
        {
            if (_selectedLineCount == 0 || _capturedToolWindow is not null)
            {
                return;
            }

            SnapshotFrame.Width = TextCaptureWidth.Clamp(SnapshotFrame.Width + e.HorizontalChange);
            StatusText.Text = $"Text screenshot width: {Math.Ceiling(SnapshotFrame.Width):0} px. Double-click the edge to reset.";
        }

        private void OnTextWidthThumbDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ResetTextWidth();
            UpdatePreviewText();
            StatusText.Text = "Text screenshot width reset to fit the selection.";
            e.Handled = true;
        }

        private void ResetTextWidth()
        {
            _isTextWidthCustomized = false;

            if (SnapshotFrame is not null)
            {
                SnapshotFrame.ClearValue(WidthProperty);
            }
        }

        private void OnZoomOutClick(object sender, RoutedEventArgs e)
            => ChangeZoom(-ZoomStep);

        private void OnZoomInClick(object sender, RoutedEventArgs e)
            => ChangeZoom(ZoomStep);

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || e.Delta == 0)
            {
                return;
            }

            ChangeZoom(e.Delta > 0 ? ZoomStep : -ZoomStep);
            e.Handled = true;
        }

        private void ChangeZoom(double change)
        {
            ZoomSlider.Value = Math.Max(ZoomSlider.Minimum, Math.Min(ZoomSlider.Maximum, ZoomSlider.Value + change));
        }

        internal void BeginCrop()
        {
            if (_capturedToolWindow is null)
            {
                return;
            }

            _annotationController.SetMode(AnnotationMode.Select);
            CancelCrop();
            _isCropping = true;
            CropLayer.Visibility = Visibility.Visible;
            CodeArea.Cursor = Cursors.Cross;
            StatusText.Text = "Crop active. Drag across the area to keep. Nearby edges snap into place.";
        }

        private void BeginCropSelection(MouseButtonEventArgs e)
        {
            _cropStart = ClampCropPoint(e.GetPosition(CodeArea));
            _cropSelection = new System.Windows.Shapes.Rectangle
            {
                Stroke = SystemColors.HighlightBrush,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Fill = new SolidColorBrush(Color.FromArgb(32, 0, 122, 204))
            };
            CropLayer.Children.Clear();
            CropLayer.Children.Add(_cropSelection);
            UpdateCropSelection(_cropStart.Value);
            CodeArea.CaptureMouse();
            e.Handled = true;
        }

        private void UpdateCropSelection(MouseEventArgs e)
        {
            if (_cropStart is null || _cropSelection is null || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            UpdateCropSelection(ClampCropPoint(e.GetPosition(CodeArea)));
            e.Handled = true;
        }

        private void UpdateCropSelection(Point end)
        {
            if (_cropStart is null || _cropSelection is null)
            {
                return;
            }

            var bounds = new Rect(_cropStart.Value, end);
            Canvas.SetLeft(_cropSelection, bounds.Left);
            Canvas.SetTop(_cropSelection, bounds.Top);
            _cropSelection.Width = bounds.Width;
            _cropSelection.Height = bounds.Height;
        }

        private void CompleteCropSelection(MouseButtonEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_cropStart is null || _capturedToolWindow is null)
            {
                return;
            }

            var bounds = new Rect(_cropStart.Value, ClampCropPoint(e.GetPosition(CodeArea)));
            var caption = _capturedToolWindow.Caption;
            CancelCrop();
            e.Handled = true;

            if (bounds.Width < 3 || bounds.Height < 3)
            {
                StatusText.Text = "Crop canceled. Choose Crop to try again.";
                return;
            }

            _annotationController.CommitTextEdit();
            RecordEditChange();

            var source = _capturedToolWindow.Image;
            var left = Math.Max(0, (int)Math.Floor(bounds.Left));
            var top = Math.Max(0, (int)Math.Floor(bounds.Top));
            var right = Math.Min(source.PixelWidth, (int)Math.Ceiling(bounds.Right));
            var bottom = Math.Min(source.PixelHeight, (int)Math.Ceiling(bounds.Bottom));
            var cropBounds = new Rect(left, top, right - left, bottom - top);
            var annotationState = _annotationController.CreateCroppedState(cropBounds);
            var cropped = new CroppedBitmap(source, new Int32Rect(left, top, right - left, bottom - top));
            cropped.Freeze();
            ShowCapturedImage(
                new ToolWindowSnapshot(cropped, caption),
                resetEditHistory: false,
                annotationState);
            StatusText.Text = $"Cropped '{caption}'. Press Ctrl+Z to undo, or copy or save the result.";
        }

        private void CancelCrop()
        {
            var wasCropping = _isCropping;
            _isCropping = false;
            _cropStart = null;
            _cropSelection = null;
            CropLayer.Children.Clear();
            CropLayer.Visibility = Visibility.Collapsed;

            if (wasCropping)
            {
                CodeArea.ReleaseMouseCapture();
                CodeArea.Cursor = Cursors.Arrow;
            }
        }

        private Point ClampCropPoint(Point point)
        {
            var x = Math.Max(0, Math.Min(CodeArea.ActualWidth, point.X));
            var y = Math.Max(0, Math.Min(CodeArea.ActualHeight, point.Y));

            if (x <= CropEdgeSnapDistance)
            {
                x = 0;
            }
            else if (CodeArea.ActualWidth - x <= CropEdgeSnapDistance)
            {
                x = CodeArea.ActualWidth;
            }

            if (y <= CropEdgeSnapDistance)
            {
                y = 0;
            }
            else if (CodeArea.ActualHeight - y <= CropEdgeSnapDistance)
            {
                y = CodeArea.ActualHeight;
            }

            return new Point(x, y);
        }

        private void OnCodeAreaMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (HasPreview == false)
            {
                return;
            }

            if (_isCropping)
            {
                BeginCropSelection(e);
                return;
            }

            if (_annotationController.IsTextEditorInput(e.OriginalSource))
            {
                return;
            }

            if (_annotationController.HandleMouseDown(e) == false && _capturedToolWindow is null)
            {
                ToggleLineHighlight(e);
            }
        }

        private void OnCodeAreaMouseMove(object sender, MouseEventArgs e)
        {
            if (_isCropping)
            {
                UpdateCropSelection(e);
                return;
            }

            _annotationController.HandleMouseMove(e);
        }

        private void OnCodeAreaMouseUp(object sender, MouseButtonEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_isCropping)
            {
                CompleteCropSelection(e);
                return;
            }

            _annotationController.HandleMouseUp(e);
        }

        // Clicking a line is the quickest way to point at it when no drawing tool is active.
        private void ToggleLineHighlight(MouseButtonEventArgs e)
        {
            var lineIndex = GetLineIndexAt(e.GetPosition(CodeArea).Y);

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

        internal bool HasPreview => _selectedLineCount > 0 || _capturedToolWindow is not null;
        internal bool CanCrop => _capturedToolWindow is not null;
        internal bool HasHighlights => _highlightedLines.Count > 0;
        internal bool HasAnnotations => _annotationController.HasAnnotations;
        internal bool CanUndoAnnotation => _annotationController.IsEditingText
            ? _annotationController.CanUndoText
            : _editHistory.CanUndo;
        internal bool CanRedoAnnotation => _annotationController.IsEditingText
            ? _annotationController.CanRedoText
            : _editHistory.CanRedo;
        internal AnnotationMode ActiveAnnotationMode => _annotationController.Mode;

        internal void SetAnnotationMode(AnnotationMode mode)
        {
            CancelCrop();
            _annotationController.SetMode(mode);
        }

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

        internal void ClearAnnotations()
            => _annotationController.Clear();

        internal void UndoAnnotation()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_annotationController.IsEditingText)
            {
                _annotationController.UndoText();
                return;
            }

            if (_editHistory.CanUndo && _editHistory.TryUndo(CaptureEditState(), out var state))
            {
                RestoreEditState(state, "Undid edit.");
            }
        }

        internal void RedoAnnotation()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_annotationController.IsEditingText)
            {
                _annotationController.RedoText();
                return;
            }

            if (_editHistory.CanRedo && _editHistory.TryRedo(CaptureEditState(), out var state))
            {
                RestoreEditState(state, "Redid edit.");
            }
        }

        private void OnAnnotationsChanging()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            RecordEditChange();
        }

        private void RecordEditChange()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (HasPreview == false)
            {
                return;
            }

            _editHistory.Record(CaptureEditState());
            UpdateCommandStatus();
        }

        private PreviewEditState CaptureEditState()
            => new PreviewEditState(_capturedToolWindow, _annotationController.CaptureState());

        private void RestoreEditState(PreviewEditState state, string status)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            CancelCrop();

            if (state.Snapshot is ToolWindowSnapshot snapshot)
            {
                ShowCapturedImage(snapshot, resetEditHistory: false, state.Annotations);
            }
            else
            {
                _annotationController.RestoreState(state.Annotations);
            }

            StatusText.Text = status;
            UpdateCommandStatus();
        }

        private void ClearEditHistory()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _editHistory.Clear();

            if (IsLoaded)
            {
                UpdateCommandStatus();
            }
        }

        private int GetLineIndexAt(double y)
        {
            if (y < 0)
            {
                return -1;
            }

            for (var index = 0; index < _previewLineRows.Count; index++)
            {
                var row = _previewLineRows[index];
                var top = row.TranslatePoint(new Point(), CodeArea).Y;

                if (y >= top && y < top + row.ActualHeight)
                {
                    return index;
                }
            }

            return -1;
        }

        private void UpdateHighlightLayers()
        {
            if (HighlightLayer is null || DimLayer is null)
            {
                return;
            }

            HighlightLayer.Children.Clear();
            DimLayer.Children.Clear();

            var width = CodeArea.ActualWidth;

            if (width <= 0 || _highlightedLines.Count == 0)
            {
                return;
            }

            var highlightGeometry = new StreamGeometry();
            var dimGeometry = new StreamGeometry { FillRule = FillRule.EvenOdd };

            using (var highlightContext = highlightGeometry.Open())
            using (var dimContext = dimGeometry.Open())
            {
                AddGeometryRectangle(dimContext, new Rect(0, 0, width, CodeArea.ActualHeight));

                foreach (var index in _highlightedLines)
                {
                    if (index < 0 || index >= _previewLineRows.Count)
                    {
                        continue;
                    }

                    var row = _previewLineRows[index];
                    var top = row.TranslatePoint(new Point(), CodeArea).Y;
                    var bounds = new Rect(0, top, width, row.ActualHeight);
                    AddGeometryRectangle(highlightContext, bounds);
                    AddGeometryRectangle(dimContext, bounds);
                }
            }

            highlightGeometry.Freeze();
            dimGeometry.Freeze();
            HighlightLayer.Children.Add(new System.Windows.Shapes.Path { Data = highlightGeometry, Fill = _highlightBrush });
            DimLayer.Children.Add(new System.Windows.Shapes.Path { Data = dimGeometry, Fill = _dimBrush });
        }

        private static void AddGeometryRectangle(StreamGeometryContext context, Rect bounds)
        {
            context.BeginFigure(bounds.TopLeft, true, true);
            context.LineTo(bounds.TopRight, true, false);
            context.LineTo(bounds.BottomRight, true, false);
            context.LineTo(bounds.BottomLeft, true, false);
        }

        // A transparent surface still has to be hit-testable, otherwise clicks fall through the
        // preview, so Brushes.Transparent is used instead of leaving the background unset.
        private Brush GetCaptureBackgroundBrush(Color themeBackground)
            => _backgroundMode switch
            {
                BackgroundMode.Transparent => Brushes.Transparent,
                BackgroundMode.Custom => new SolidColorBrush(_backgroundColor),
                BackgroundMode.Gradient => CreateGradientBrush(_gradientStartColor, _gradientEndColor, _gradientAngle),
                _ => CreateBackdropBrush(themeBackground)
            };

        // The angle is given in degrees rather than as two points because that is how a gradient is
        // described everywhere else, and the line is projected onto the unit square so that the
        // colors reach the corners instead of running out partway across a wide screenshot.
        private static Brush CreateGradientBrush(Color start, Color end, int angle)
        {
            var radians = angle * Math.PI / 180;
            var dx = Math.Cos(radians);
            var dy = Math.Sin(radians);

            // Half the span the gradient line covers once it has to span the whole square.
            var reach = (Math.Abs(dx) + Math.Abs(dy)) / 2;

            return new LinearGradientBrush(
                start,
                end,
                new Point(0.5 - (dx * reach), 0.5 - (dy * reach)),
                new Point(0.5 + (dx * reach), 0.5 + (dy * reach)));
        }

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
            var lines = new List<SelectedLinePart>(lastLine - firstLine + 1);

            for (var lineNumber = firstLine; lineNumber <= lastLine; lineNumber++)
            {
                var (line, start, end) = GetSelectedLinePart(snapshot, lineNumber, selectionSpan);
                lines.Add(new SelectedLinePart(line.Start.Position, start, end));
            }

            return SelectionTextProcessor.Normalize(lines, keepOriginalIndentation, position => snapshot[position])
                .Select(segment => new SnapshotSpan(snapshot, segment.Start, segment.Length))
                .ToList();
        }

        private static (ITextSnapshotLine line, int start, int end) GetSelectedLinePart(ITextSnapshot snapshot, int lineNumber, SnapshotSpan selectionSpan)
        {
            var line = snapshot.GetLineFromLineNumber(lineNumber);
            var start = Math.Max(line.Start.Position, selectionSpan.Start.Position);
            var end = Math.Max(start, Math.Min(line.End.Position, selectionSpan.End.Position));
            return (line, start, end);
        }

        private IReadOnlyList<IReadOnlyList<Inline>>? BuildClassifiedLines(IWpfTextView textView, IReadOnlyList<SnapshotSpan> selectedSpans)
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

            var lines = new List<IReadOnlyList<Inline>>(selectedSpans.Count);

            // Classifying each line separately means one service call per line, so the whole
            // selection is classified once and the ordered result is sliced per line.
            var enclosingSpan = new SnapshotSpan(selectedSpans[0].Start, selectedSpans[selectedSpans.Count - 1].End);
            var classificationSpans = classifier.GetClassificationSpans(enclosingSpan);
            var classificationIndex = 0;

            for (var index = 0; index < selectedSpans.Count; index++)
            {
                var inlines = new List<Inline>();
                AppendClassifiedSpanRuns(inlines, classificationSpans, ref classificationIndex, formatMap, selectedSpans[index], defaultForeground, defaultBackground);
                lines.Add(inlines);
            }

            return lines;
        }

        private void ApplyEditorColors(Brush? foreground, Brush? background)
        {
            if (foreground is null && background is null)
            {
                return;
            }

            ApplySnapshotColors(foreground ?? _previewForeground, background ?? SnapshotFrame.Background);
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
            if (_capturedToolWindow is not null)
            {
                return;
            }

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
                _gradientStartColor = ParseColor(options.GradientStartColor, _gradientStartColor);
                _gradientEndColor = ParseColor(options.GradientEndColor, _gradientEndColor);
                _gradientAngle = options.GradientAngle;
                _saveFolder = options.SaveFolder ?? string.Empty;
                _saveFileNameTemplate = string.IsNullOrWhiteSpace(options.SaveFileNameTemplate) ? "{fileNameWithoutExtension}" : options.SaveFileNameTemplate;
                _promptForSaveLocation = options.PromptForSaveLocation;
                _lineHeightMultiplier = ClampLineHeight(options.LineHeight);
                ApplyWindowControls(options.WindowControls);
                ApplyShape(options.CornerRadius, options.ShowShadow);
                ApplyPadding(options.Padding);
                PreviewFontFamily = string.IsNullOrWhiteSpace(options.FontFamily) ? editorFamily : options.FontFamily;
                PreviewFontSize = options.FontSize <= 0 ? editorSize : options.FontSize;
                ShowTitleBar = options.ShowTitleBar;
                ShowLineNumbers = options.ShowLineNumbers;
                UseRealLineNumbers = options.UseRealLineNumbers;
                KeepOriginalIndentation = options.KeepOriginalIndentation;

                // The font settings are only reapplied by their own setters, which do nothing when
                // the font itself has not changed, so a new line height needs its own pass.
                ApplyFontSettings();
            }
            finally
            {
                _isApplyingOptions = false;
            }

            ApplyTheme();
            UpdateScreenshotDimensions();
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

            var appliedRadius = _capturedToolWindow is null ? _cornerRadius : 0;
            SnapshotFrame.CornerRadius = new CornerRadius(appliedRadius);
            ShadowHost.CornerRadius = new CornerRadius(appliedRadius);
            TitleBarBorder.CornerRadius = new CornerRadius(appliedRadius, appliedRadius, 0, 0);

            // The outer surface sits behind the frame, so it needs the larger radius of the two
            // to avoid a square corner peeking out from under a rounded one.
            CaptureSurface.CornerRadius = new CornerRadius(appliedRadius == 0 ? 0 : appliedRadius + 3);

            ApplyShadow();
        }

        // The shadow is drawn inside the capture surface, so without padding it would be clipped
        // away and only cost render time.
        private void ApplyShadow()
        {
            if (_capturedToolWindow is not null || _showShadow == false || _padding < ShadowMinimumPadding)
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
            CaptureSurface.Padding = new Thickness(_capturedToolWindow is null ? _padding : 0);
            ApplyShadow();
        }

        private static double ClampExportScale(double value)
            => double.IsNaN(value) || double.IsInfinity(value) || value <= 0
                ? 1d
                : Math.Min(8d, value);

        // Below one the lines overlap and above three the code stops reading as a block, and either
        // way the highlight overlays are sized from the line height and would no longer line up.
        private static double ClampLineHeight(double value)
            => double.IsNaN(value) || double.IsInfinity(value) || value <= 0
                ? 1.45d
                : Math.Min(3d, Math.Max(1d, value));

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
                    options.SaveFolder = _saveFolder;
                    await options.SaveAsync();
                },
                "Could not save the CodeShot options.");
        }

        private void ApplyFontSettings()
        {
            var fontFamily = new FontFamily(_fontFamilyName);

            // The default line spacing of a monospaced font is tight enough that a screenshot reads
            // as a wall of text, so the lines are opened up the way a code sample in an article is.
            var lineHeight = Math.Round(_fontSize * _lineHeightMultiplier);

            foreach (var textBlock in _previewTextBlocks.Concat(_lineNumberTextBlocks))
            {
                textBlock.FontFamily = fontFamily;
                textBlock.FontSize = _fontSize;
                textBlock.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                textBlock.LineHeight = lineHeight;
                textBlock.MinHeight = lineHeight;
            }

            foreach (var paragraph in _previewParagraphs)
            {
                var continuationIndent = MeasureTextWidth(paragraph.Tag as string ?? "  ", fontFamily);
                paragraph.FontFamily = fontFamily;
                paragraph.FontSize = _fontSize;
                paragraph.LineHeight = lineHeight;
                paragraph.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                paragraph.Margin = new Thickness(continuationIndent, 0, 0, 0);
                paragraph.TextIndent = -continuationIndent;
            }
        }

        private double MeasureTextWidth(string text, FontFamily fontFamily)
        {
            var formattedText = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                _fontSize,
                Brushes.Black,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            return formattedText.WidthIncludingTrailingWhitespace;
        }

        private void ApplyPreviewTextColors()
        {
            foreach (var paragraph in _previewParagraphs)
            {
                paragraph.Foreground = _previewForeground;
            }

            foreach (var textBlock in _previewTextBlocks)
            {
                textBlock.Foreground = _previewForeground;
            }

            foreach (var textBlock in _lineNumberTextBlocks)
            {
                textBlock.Foreground = _lineNumberForeground;
            }
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
                HideLoading();
                StatusText.Text = userMessage;
            }
        }

        private sealed class PreviewEditState
        {
            internal PreviewEditState(ToolWindowSnapshot? snapshot, AnnotationController.State annotations)
            {
                Snapshot = snapshot;
                Annotations = annotations;
            }

            internal ToolWindowSnapshot? Snapshot { get; }
            internal AnnotationController.State Annotations { get; }
        }

        private readonly struct PreviewSpanIdentity
        {
            private readonly int _snapshotVersion;
            private readonly int _start;
            private readonly int _length;

            internal PreviewSpanIdentity(SnapshotSpan span)
            {
                _snapshotVersion = span.Snapshot.Version.VersionNumber;
                _start = span.Start.Position;
                _length = span.Length;
            }

            internal bool Matches(SnapshotSpan span)
                => _snapshotVersion == span.Snapshot.Version.VersionNumber
                    && _start == span.Start.Position
                    && _length == span.Length;
        }
    }
}
