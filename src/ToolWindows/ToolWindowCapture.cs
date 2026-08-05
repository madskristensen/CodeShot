using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using Media = System.Windows.Media;

namespace CodeShot.ToolWindows
{
    internal static class ToolWindowCapture
    {
        private const int MaximumCaptureDimension = 16384;
        private const long MaximumCapturePixelCount = 32_000_000;
        private static readonly SemaphoreSlim CaptureGate = new SemaphoreSlim(1, 1);

        internal static async Task<ToolWindowSnapshot?> CaptureCurrentAsync()
        {
            await CaptureGate.WaitAsync();

            try
            {
                return await CaptureCurrentCoreAsync();
            }
            finally
            {
                CaptureGate.Release();
            }
        }

        private static async Task<ToolWindowSnapshot?> CaptureCurrentCoreAsync()
        {
            var monitorSelection = await VS.GetServiceAsync<SVsShellMonitorSelection, IVsMonitorSelection>();
            if (monitorSelection is null)
            {
                return null;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (TryGetCurrentFrame(monitorSelection, out var frame) == false)
            {
                return null;
            }

            // Let the shell dismiss its context menu before resolving the final frame geometry.
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            if (TryGetCurrentFrame(monitorSelection, out var currentFrame) == false
                || IsSameComObject(frame, currentFrame) == false
                || TryGetContentBounds(currentFrame, out var contentBounds) == false)
            {
                return null;
            }

            var caption = GetCaption(currentFrame);
            var rootWindow = GetRootWindow(contentBounds);
            if (rootWindow == IntPtr.Zero || IsWindow(rootWindow) == false || IsWindowVisible(rootWindow) == false)
            {
                return null;
            }

            var x = contentBounds.X;
            var y = contentBounds.Y;
            var width = contentBounds.Width;
            var height = contentBounds.Height;
            var shellBorder = IncludeShellFrame(ref x, ref y, ref width, ref height, out var cornerRadius);
            var captureBounds = new System.Drawing.Rectangle(x, y, width, height);
            var chromeBounds = await FindToolWindowChromeBoundsAsync(caption, contentBounds, captureBounds);
            var selectedTabBounds = chromeBounds?.TabBounds;
            var outerInset = Math.Max(1, shellBorder - 1);

            if (chromeBounds?.FrameBounds is System.Drawing.Rectangle exactFrame
                && exactFrame.Width > 0
                && exactFrame.Height > 0)
            {
                captureBounds = exactFrame;
                outerInset = 0;
            }
            else if (selectedTabBounds is System.Drawing.Rectangle selectedTab)
            {
                var requiredBottom = selectedTab.Bottom + shellBorder;
                captureBounds.Height = Math.Max(captureBounds.Bottom, requiredBottom) - captureBounds.Top;
            }

            if (IsCaptureSizeSupported(captureBounds) == false)
            {
                return null;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // UI Automation runs off-thread. Refuse to copy pixels if its target moved, closed,
            // auto-hid, or lost selection while that work was in progress.
            if (TryGetCurrentFrame(monitorSelection, out currentFrame) == false
                || IsSameComObject(frame, currentFrame) == false
                || TryGetContentBounds(currentFrame, out var currentBounds) == false
                || currentBounds != contentBounds
                || GetRootWindow(currentBounds) != rootWindow
                || IsWindow(rootWindow) == false
                || IsWindowVisible(rootWindow) == false)
            {
                return null;
            }

            var visualMask = CreateVisualMask(chromeBounds, captureBounds, contentBounds);
            var bitmap = CaptureScreen(
                captureBounds,
                outerInset,
                cornerRadius,
                selectedTabBounds,
                visualMask);
            return new ToolWindowSnapshot(bitmap, caption);
        }

        private static bool TryGetCurrentFrame(IVsMonitorSelection monitorSelection, out IVsWindowFrame frame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = monitorSelection.GetCurrentElementValue(
                (uint)VSConstants.VSSELELEMID.SEID_WindowFrame,
                out var frameValue);

            if (ErrorHandler.Succeeded(result) && frameValue is IVsWindowFrame currentFrame)
            {
                frame = currentFrame;
                return true;
            }

            frame = null!;
            return false;
        }

        private static bool TryGetContentBounds(IVsWindowFrame frame, out System.Drawing.Rectangle bounds)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (frame is IVsWindowFrame4 frame4
                && frame4.GetWindowScreenRect(out var x, out var y, out var width, out var height)
                && width > 0
                && height > 0)
            {
                bounds = new System.Drawing.Rectangle(x, y, width, height);
                return true;
            }

            bounds = System.Drawing.Rectangle.Empty;
            return false;
        }

        private static bool IsSameComObject(object first, object second)
        {
            var firstIdentity = IntPtr.Zero;
            var secondIdentity = IntPtr.Zero;

            try
            {
                firstIdentity = Marshal.GetIUnknownForObject(first);
                secondIdentity = Marshal.GetIUnknownForObject(second);
                return firstIdentity == secondIdentity;
            }
            finally
            {
                if (firstIdentity != IntPtr.Zero)
                {
                    Marshal.Release(firstIdentity);
                }

                if (secondIdentity != IntPtr.Zero)
                {
                    Marshal.Release(secondIdentity);
                }
            }
        }

        private static IntPtr GetRootWindow(System.Drawing.Rectangle contentBounds)
        {
            var point = new NativePoint
            {
                X = contentBounds.Left + (contentBounds.Width / 2),
                Y = contentBounds.Top
            };
            var window = WindowFromPoint(point);
            return window == IntPtr.Zero ? IntPtr.Zero : GetAncestor(window, GetAncestorRoot);
        }

        internal static bool IsCaptureSizeSupported(System.Drawing.Rectangle bounds)
            => bounds.Width > 0
                && bounds.Height > 0
                && bounds.Width <= MaximumCaptureDimension
                && bounds.Height <= MaximumCaptureDimension
                && (long)bounds.Width * bounds.Height <= MaximumCapturePixelCount;

        private static int IncludeShellFrame(
            ref int x,
            ref int y,
            ref int width,
            ref int height,
            out int cornerRadius)
        {
            var point = new NativePoint
            {
                X = x + (width / 2),
                Y = y
            };
            var window = WindowFromPoint(point);
            var dpi = window == IntPtr.Zero ? 96u : GetDpiForWindow(window);
            var scale = dpi > 0 ? dpi / 96d : 1d;
            var border = Math.Max(2, (int)Math.Ceiling(SystemParameters.BorderWidth * scale));
            var captionHeight = Math.Max(1, (int)Math.Ceiling(SystemParameters.SmallCaptionHeight * scale));
            var tabHeight = captionHeight;
            cornerRadius = Math.Max(border * 4, (int)Math.Ceiling(8 * scale));

            x -= border;
            y -= captionHeight + (border * 2);
            width += border * 2;
            height += captionHeight + tabHeight + (border * 3);
            return border;
        }

        private static async Task<ToolWindowChromeBounds?> FindToolWindowChromeBoundsAsync(
            string caption,
            System.Drawing.Rectangle contentBounds,
            System.Drawing.Rectangle captureBounds)
        {
            var rootWindow = GetRootWindow(contentBounds);

            if (rootWindow == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return await Task.Run(
                    () => FindToolWindowChromeBounds(rootWindow, caption, contentBounds, captureBounds));
            }
            catch (ElementNotAvailableException ex)
            {
                await ex.LogAsync();
                return null;
            }
            catch (InvalidOperationException ex)
            {
                await ex.LogAsync();
                return null;
            }
            catch (COMException ex)
            {
                await ex.LogAsync();
                return null;
            }
        }

        private static ToolWindowChromeBounds? FindToolWindowChromeBounds(
            IntPtr rootWindow,
            string caption,
            System.Drawing.Rectangle contentBounds,
            System.Drawing.Rectangle captureBounds)
        {
            var root = AutomationElement.FromHandle(rootWindow);
            var condition = new AndCondition(
                new PropertyCondition(AutomationElement.NameProperty, caption),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));
            var matches = root.FindAll(TreeScope.Descendants, condition);
            var expectedStrip = System.Drawing.Rectangle.FromLTRB(
                captureBounds.Left,
                contentBounds.Bottom - 2,
                captureBounds.Right,
                captureBounds.Bottom);

            for (var index = 0; index < matches.Count; index++)
            {
                var bounds = matches[index].Current.BoundingRectangle;
                if (bounds.IsEmpty)
                {
                    continue;
                }

                var candidate = System.Drawing.Rectangle.FromLTRB(
                    (int)Math.Floor(bounds.Left),
                    (int)Math.Floor(bounds.Top),
                    (int)Math.Ceiling(bounds.Right),
                    (int)Math.Ceiling(bounds.Bottom));

                if (candidate.IntersectsWith(expectedStrip))
                {
                    return new ToolWindowChromeBounds(
                        rootWindow,
                        candidate,
                        FindDockingContainerBounds(matches[index]));
                }
            }

            return null;
        }

        private static System.Drawing.Rectangle? FindDockingContainerBounds(AutomationElement tab)
        {
            var walker = TreeWalker.ControlViewWalker;
            var element = walker.GetParent(tab);

            while (element is not null)
            {
                if (string.Equals(
                    element.Current.ClassName,
                    "ToolWindowTabGroupContainer",
                    StringComparison.Ordinal))
                {
                    var bounds = element.Current.BoundingRectangle;
                    if (bounds.IsEmpty == false)
                    {
                        return System.Drawing.Rectangle.FromLTRB(
                            (int)Math.Floor(bounds.Left),
                            (int)Math.Floor(bounds.Top),
                            (int)Math.Ceiling(bounds.Right),
                            (int)Math.Ceiling(bounds.Bottom));
                    }
                }

                element = walker.GetParent(element);
            }

            return null;
        }

        private static byte[]? CreateVisualMask(
            ToolWindowChromeBounds? chromeBounds,
            System.Drawing.Rectangle captureBounds,
            System.Drawing.Rectangle contentBounds)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (chromeBounds is not ToolWindowChromeBounds chrome
                || chrome.FrameBounds is not System.Drawing.Rectangle frameBounds)
            {
                return null;
            }

            var source = HwndSource.FromHwnd(chrome.RootWindow);
            if (source?.RootVisual is not Media.Visual rootVisual)
            {
                return null;
            }

            var container = FindMatchingVisual(
                rootVisual,
                frameBounds,
                "Microsoft.VisualStudio.PlatformUI.Shell.Controls.TabGroupControl");
            if (container is null)
            {
                return null;
            }

            var mask = RenderVisualMask(container, captureBounds);
            if (HasRoundedTopCorners(mask, captureBounds.Width, captureBounds.Height) == false)
            {
                return null;
            }

            MakeMaskRectangleOpaque(mask, captureBounds, contentBounds);

            var tab = FindMatchingVisual(
                rootVisual,
                chrome.TabBounds,
                "Microsoft.VisualStudio.PlatformUI.Shell.Controls.GroupControlTabItem");
            if (tab is not null)
            {
                var tabMask = RenderVisualMask(tab, captureBounds);
                if (HasVisibleTab(tabMask, captureBounds, chrome.TabBounds))
                {
                    ReplaceTabMask(mask, tabMask, captureBounds, chrome.TabBounds);
                }
            }

            ClearSiblingTabMask(mask, captureBounds, chrome.TabBounds);
            return mask;
        }

        private static FrameworkElement? FindMatchingVisual(
            Media.Visual root,
            System.Drawing.Rectangle expectedBounds,
            string typeName)
        {
            FrameworkElement? bestMatch = null;
            var bestScore = int.MaxValue;
            FindMatchingVisual(root, expectedBounds, typeName, ref bestMatch, ref bestScore);
            return bestMatch;
        }

        private static void FindMatchingVisual(
            DependencyObject current,
            System.Drawing.Rectangle expectedBounds,
            string typeName,
            ref FrameworkElement? bestMatch,
            ref int bestScore)
        {
            if (current is FrameworkElement element
                && element.IsVisible
                && element.ActualWidth > 0
                && element.ActualHeight > 0
                && string.Equals(element.GetType().FullName, typeName, StringComparison.Ordinal))
            {
                var bounds = GetVisualScreenBounds(element);
                var score = Math.Abs(bounds.Left - expectedBounds.Left)
                    + Math.Abs(bounds.Top - expectedBounds.Top)
                    + Math.Abs(bounds.Right - expectedBounds.Right)
                    + Math.Abs(bounds.Bottom - expectedBounds.Bottom);

                if (score <= 12 && score < bestScore)
                {
                    bestMatch = element;
                    bestScore = score;
                }
            }

            var childCount = Media.VisualTreeHelper.GetChildrenCount(current);
            for (var index = 0; index < childCount; index++)
            {
                FindMatchingVisual(
                    Media.VisualTreeHelper.GetChild(current, index),
                    expectedBounds,
                    typeName,
                    ref bestMatch,
                    ref bestScore);
            }
        }

        private static System.Drawing.Rectangle GetVisualScreenBounds(FrameworkElement element)
        {
            var topLeft = element.PointToScreen(new System.Windows.Point(0, 0));
            var bottomRight = element.PointToScreen(
                new System.Windows.Point(element.ActualWidth, element.ActualHeight));
            return System.Drawing.Rectangle.FromLTRB(
                (int)Math.Floor(topLeft.X),
                (int)Math.Floor(topLeft.Y),
                (int)Math.Ceiling(bottomRight.X),
                (int)Math.Ceiling(bottomRight.Y));
        }

        private static byte[] RenderVisualMask(
            FrameworkElement element,
            System.Drawing.Rectangle captureBounds)
        {
            var elementBounds = GetVisualScreenBounds(element);
            var drawing = new Media.DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                var brush = new Media.VisualBrush(element)
                {
                    AlignmentX = Media.AlignmentX.Left,
                    AlignmentY = Media.AlignmentY.Top,
                    Stretch = Media.Stretch.Fill,
                    Viewbox = new Rect(0, 0, element.ActualWidth, element.ActualHeight),
                    ViewboxUnits = Media.BrushMappingMode.Absolute
                };
                context.DrawRectangle(
                    brush,
                    null,
                    new Rect(
                        elementBounds.Left - captureBounds.Left,
                        elementBounds.Top - captureBounds.Top,
                        elementBounds.Width,
                        elementBounds.Height));
            }

            var renderTarget = new RenderTargetBitmap(
                captureBounds.Width,
                captureBounds.Height,
                96,
                96,
                Media.PixelFormats.Pbgra32);
            renderTarget.Render(drawing);

            var stride = captureBounds.Width * 4;
            var pixels = new byte[stride * captureBounds.Height];
            renderTarget.CopyPixels(pixels, stride, 0);
            return pixels;
        }

        private static bool HasRoundedTopCorners(byte[] mask, int width, int height)
        {
            if (width < 3 || height < 1)
            {
                return false;
            }

            var topCenterAlpha = mask[((width / 2) * 4) + 3];
            var topLeftAlpha = mask[3];
            var topRightAlpha = mask[((width - 1) * 4) + 3];
            return topCenterAlpha > 0 && (topLeftAlpha < byte.MaxValue || topRightAlpha < byte.MaxValue);
        }

        private static bool HasVisibleTab(
            byte[] tabMask,
            System.Drawing.Rectangle captureBounds,
            System.Drawing.Rectangle tabBounds)
        {
            var centerX = Math.Max(0, Math.Min(captureBounds.Width - 1, tabBounds.Left - captureBounds.Left + (tabBounds.Width / 2)));
            var centerY = Math.Max(0, Math.Min(captureBounds.Height - 1, tabBounds.Top - captureBounds.Top + (tabBounds.Height / 2)));
            return tabMask[(((centerY * captureBounds.Width) + centerX) * 4) + 3] > 0;
        }

        private static void MakeMaskRectangleOpaque(
            byte[] mask,
            System.Drawing.Rectangle captureBounds,
            System.Drawing.Rectangle screenBounds)
        {
            var bounds = System.Drawing.Rectangle.Intersect(captureBounds, screenBounds);
            for (var y = bounds.Top; y < bounds.Bottom; y++)
            {
                for (var x = bounds.Left; x < bounds.Right; x++)
                {
                    var offset = ((((y - captureBounds.Top) * captureBounds.Width) + x - captureBounds.Left) * 4) + 3;
                    mask[offset] = byte.MaxValue;
                }
            }
        }

        private static void ReplaceTabMask(
            byte[] mask,
            byte[] tabMask,
            System.Drawing.Rectangle captureBounds,
            System.Drawing.Rectangle tabBounds)
        {
            var bounds = System.Drawing.Rectangle.Intersect(captureBounds, tabBounds);
            for (var y = bounds.Top; y < bounds.Bottom; y++)
            {
                var offset = (((y - captureBounds.Top) * captureBounds.Width) + bounds.Left - captureBounds.Left) * 4;
                var length = bounds.Width * 4;
                Buffer.BlockCopy(tabMask, offset, mask, offset, length);
            }
        }

        private static void ClearSiblingTabMask(
            byte[] mask,
            System.Drawing.Rectangle captureBounds,
            System.Drawing.Rectangle selectedTabBounds)
        {
            var stripTop = Math.Max(0, selectedTabBounds.Top - captureBounds.Top + 1);
            var selectedLeft = Math.Max(0, selectedTabBounds.Left - captureBounds.Left);
            var selectedRight = Math.Min(captureBounds.Width, selectedTabBounds.Right - captureBounds.Left);

            for (var y = stripTop; y < captureBounds.Height; y++)
            {
                for (var x = 0; x < captureBounds.Width; x++)
                {
                    if (x >= selectedLeft && x < selectedRight)
                    {
                        continue;
                    }

                    var offset = ((y * captureBounds.Width) + x) * 4;
                    mask[offset] = 0;
                    mask[offset + 1] = 0;
                    mask[offset + 2] = 0;
                    mask[offset + 3] = 0;
                }
            }
        }

        private static System.Drawing.Rectangle ApplyVisualMask(Bitmap bitmap, byte[] visualMask)
        {
            var bounds = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var opaqueBounds = bounds;
            var data = bitmap.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            try
            {
                var stride = Math.Abs(data.Stride);
                var pixels = new byte[stride * bitmap.Height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

                for (var y = 0; y < bitmap.Height; y++)
                {
                    for (var x = 0; x < bitmap.Width; x++)
                    {
                        var bitmapOffset = GetPixelOffset(x, y, bitmap.Height, data.Stride, stride);
                        var maskOffset = ((y * bitmap.Width) + x) * 4;
                        var alpha = visualMask[maskOffset + 3];

                        if (alpha == byte.MaxValue)
                        {
                            pixels[bitmapOffset + 3] = byte.MaxValue;
                        }
                        else
                        {
                            pixels[bitmapOffset] = visualMask[maskOffset];
                            pixels[bitmapOffset + 1] = visualMask[maskOffset + 1];
                            pixels[bitmapOffset + 2] = visualMask[maskOffset + 2];
                            pixels[bitmapOffset + 3] = alpha;
                        }
                    }
                }

                opaqueBounds = FindOpaqueBounds(
                    bitmap.Width,
                    bitmap.Height,
                    data.Stride,
                    stride,
                    pixels);
                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return opaqueBounds;
        }

        internal static BitmapSource CaptureScreen(System.Drawing.Rectangle captureBounds)
        {
            return CaptureScreen(captureBounds, 0, 0, null, null);
        }

        internal static BitmapSource CaptureScreen(
            System.Drawing.Rectangle captureBounds,
            IReadOnlyList<System.Drawing.Rectangle> visibleRegions)
        {
            var width = captureBounds.Width;
            var height = captureBounds.Height;
            var mask = new byte[width * height * 4];

            for (var regionIndex = 0; regionIndex < visibleRegions.Count; regionIndex++)
            {
                var region = System.Drawing.Rectangle.Intersect(captureBounds, visibleRegions[regionIndex]);
                for (var y = region.Top; y < region.Bottom; y++)
                {
                    for (var x = region.Left; x < region.Right; x++)
                    {
                        var offset = ((((y - captureBounds.Top) * width) + x - captureBounds.Left) * 4) + 3;
                        mask[offset] = byte.MaxValue;
                    }
                }
            }

            return CaptureScreen(captureBounds, 0, 0, null, mask);
        }

        private static BitmapSource CaptureScreen(
            System.Drawing.Rectangle captureBounds,
            int outerInset,
            int cornerRadius,
            System.Drawing.Rectangle? selectedTabBounds,
            byte[]? visualMask)
        {
            using (var screen = new Bitmap(captureBounds.Width, captureBounds.Height, PixelFormat.Format32bppRgb))
            using (var screenGraphics = Graphics.FromImage(screen))
            {
                screenGraphics.CopyFromScreen(
                    captureBounds.Left,
                    captureBounds.Top,
                    0,
                    0,
                    captureBounds.Size,
                    CopyPixelOperation.SourceCopy);

                using (var bitmap = new Bitmap(captureBounds.Width, captureBounds.Height, PixelFormat.Format32bppPArgb))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.DrawImageUnscaled(screen, 0, 0);
                    System.Drawing.Rectangle opaqueBounds;

                    if (visualMask is not null)
                    {
                        graphics.Flush();
                        opaqueBounds = ApplyVisualMask(bitmap, visualMask);
                    }
                    else
                    {
                        MaskSiblingTabs(graphics, captureBounds, selectedTabBounds);

                        if (outerInset > 0)
                        {
                            MaskFrameEdges(graphics, bitmap.Width, bitmap.Height, outerInset, cornerRadius);
                        }

                        graphics.Flush();
                        opaqueBounds = FindOpaqueBounds(bitmap);
                    }

                    var data = bitmap.LockBits(
                        new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                        ImageLockMode.ReadOnly,
                        PixelFormat.Format32bppPArgb);
                    try
                    {
                        var source = BitmapSource.Create(
                            bitmap.Width,
                            bitmap.Height,
                            96,
                            96,
                            System.Windows.Media.PixelFormats.Pbgra32,
                            null,
                            data.Scan0,
                            Math.Abs(data.Stride) * bitmap.Height,
                            data.Stride);
                        source.Freeze();

                        if (opaqueBounds.IsEmpty
                            || opaqueBounds == new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height))
                        {
                            return source;
                        }

                        var cropped = new CroppedBitmap(
                            source,
                            new Int32Rect(
                                opaqueBounds.X,
                                opaqueBounds.Y,
                                opaqueBounds.Width,
                                opaqueBounds.Height));
                        cropped.Freeze();
                        return cropped;
                    }
                    finally
                    {
                        bitmap.UnlockBits(data);
                    }
                }
            }
        }

        private static void MaskFrameEdges(
            Graphics graphics,
            int width,
            int height,
            int inset,
            int cornerRadius)
        {
            if (width <= 2 || height <= 2)
            {
                return;
            }
            var radius = Math.Min(cornerRadius, Math.Min((width - (inset * 2)) / 2, height - inset));
            var diameter = radius * 2;
            using (var frame = new GraphicsPath())
            using (var outside = new Region(new System.Drawing.Rectangle(0, 0, width, height)))
            using (var transparent = new SolidBrush(System.Drawing.Color.Transparent))
            {
                frame.StartFigure();
                frame.AddArc(inset, inset, diameter, diameter, 180, 90);
                frame.AddLine(inset + radius, inset, width - inset - radius, inset);
                frame.AddArc(width - inset - diameter, inset, diameter, diameter, 270, 90);
                frame.AddLine(width - inset, inset + radius, width - inset, height);
                frame.AddLine(width - inset, height, inset, height);
                frame.CloseFigure();

                outside.Exclude(frame);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.FillRegion(transparent, outside);
            }
        }

        private static void MaskSiblingTabs(
            Graphics graphics,
            System.Drawing.Rectangle captureBounds,
            System.Drawing.Rectangle? selectedTabBounds)
        {
            if (selectedTabBounds is not System.Drawing.Rectangle selectedTab)
            {
                return;
            }

            var tabTop = Math.Max(0, selectedTab.Top - captureBounds.Top);
            var stripTop = Math.Min(captureBounds.Height, tabTop + 1);
            var selectedLeft = Math.Max(0, selectedTab.Left - captureBounds.Left);
            var selectedRight = Math.Min(captureBounds.Width, selectedTab.Right - captureBounds.Left);
            var selectedBottom = Math.Min(captureBounds.Height, selectedTab.Bottom - captureBounds.Top);
            var stripHeight = captureBounds.Height - stripTop;

            if (stripHeight <= 0 || selectedRight <= selectedLeft || selectedBottom <= stripTop)
            {
                return;
            }

            graphics.CompositingMode = CompositingMode.SourceCopy;
            using (var transparent = new SolidBrush(System.Drawing.Color.Transparent))
            {
                graphics.FillRectangle(transparent, 0, stripTop, selectedLeft, stripHeight);
                graphics.FillRectangle(
                    transparent,
                    selectedRight,
                    stripTop,
                    captureBounds.Width - selectedRight,
                    stripHeight);
                graphics.FillRectangle(
                    transparent,
                    selectedLeft,
                    selectedBottom,
                    selectedRight - selectedLeft,
                    captureBounds.Height - selectedBottom);
            }
        }

        private static System.Drawing.Rectangle FindOpaqueBounds(Bitmap bitmap)
        {
            var bounds = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                var stride = Math.Abs(data.Stride);
                var pixels = new byte[stride * bitmap.Height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                return FindOpaqueBounds(
                    bitmap.Width,
                    bitmap.Height,
                    data.Stride,
                    stride,
                    pixels);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static System.Drawing.Rectangle FindOpaqueBounds(
            int width,
            int height,
            int signedStride,
            int stride,
            byte[] pixels)
        {
            var left = width;
            var top = height;
            var right = -1;
            var bottom = -1;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var offset = GetPixelOffset(x, y, height, signedStride, stride);
                    if (pixels[offset + 3] == 0)
                    {
                        continue;
                    }

                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }

            return right < left || bottom < top
                ? System.Drawing.Rectangle.Empty
                : System.Drawing.Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }

        private static int GetPixelOffset(int x, int y, int height, int signedStride, int stride)
        {
            var row = signedStride < 0 ? height - y - 1 : y;
            return (row * stride) + (x * 4);
        }

        private readonly struct ToolWindowChromeBounds
        {
            internal ToolWindowChromeBounds(
                IntPtr rootWindow,
                System.Drawing.Rectangle tabBounds,
                System.Drawing.Rectangle? frameBounds)
            {
                RootWindow = rootWindow;
                TabBounds = tabBounds;
                FrameBounds = frameBounds;
            }

            internal IntPtr RootWindow { get; }
            internal System.Drawing.Rectangle TabBounds { get; }
            internal System.Drawing.Rectangle? FrameBounds { get; }
        }

        private static string GetCaption(IVsWindowFrame frame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (ErrorHandler.Succeeded(frame.GetProperty((int)__VSFPROPID.VSFPROPID_Caption, out var caption))
                && caption is string text
                && string.IsNullOrWhiteSpace(text) == false)
            {
                return text;
            }

            return "Tool Window";
        }

        private const uint GetAncestorRoot = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flags);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(NativePoint point);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            internal int X;
            internal int Y;
        }
    }
}
