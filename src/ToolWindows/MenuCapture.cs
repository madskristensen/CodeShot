using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace CodeShot.ToolWindows
{
    internal static class MenuCapture
    {
        private const string NativeMenuWindowClass = "#32768";
        private const int ExtendedFrameBoundsAttribute = 9;
        private const int MenuAdjacencyTolerance = 12;
        private const int TopLevelRowTolerance = 4;
        private const uint GetAncestorRoot = 2;
        private const uint GetWindowOwner = 4;

        internal static IntPtr GetVisualStudioMainWindow()
        {
            return Process.GetCurrentProcess().MainWindowHandle;
        }

        internal static Task<ToolWindowSnapshot?> CaptureAsync(IntPtr visualStudioMainWindow)
        {
            return Task.Run(() => Capture(visualStudioMainWindow));
        }

        private static ToolWindowSnapshot? Capture(IntPtr visualStudioMainWindow)
        {
            var processId = Process.GetCurrentProcess().Id;
            var captureRegions = new List<Rectangle>();
            Rectangle? captureBounds = null;

            if (TryAddTopmostVisual(
                processId,
                visualStudioMainWindow,
                ref captureBounds,
                captureRegions) == false)
            {
                _ = AddAutomationBounds(processId, ref captureBounds, captureRegions);
                AddNativeMenuBounds(processId, ref captureBounds, captureRegions);
            }

            if (captureBounds is not Rectangle bounds
                || ToolWindowCapture.IsCaptureSizeSupported(bounds) == false)
            {
                return null;
            }

            return new ToolWindowSnapshot(
                ToolWindowCapture.CaptureScreen(bounds, captureRegions),
                "Visual Studio Menu");
        }

        private static bool TryAddTopmostVisual(
            int processId,
            IntPtr visualStudioMainWindow,
            ref Rectangle? captureBounds,
            List<Rectangle> captureRegions)
        {
            if (GetCursorPos(out var cursor) == false)
            {
                return false;
            }

            var cursorWindow = WindowFromPoint(cursor);
            var cursorRoot = cursorWindow == IntPtr.Zero
                ? IntPtr.Zero
                : GetAncestor(cursorWindow, GetAncestorRoot);
            var foregroundWindow = GetForegroundWindow();
            var foregroundRoot = foregroundWindow == IntPtr.Zero
                ? IntPtr.Zero
                : GetAncestor(foregroundWindow, GetAncestorRoot);
            var cursorIsMenu = IsVisualStudioWindow(cursorRoot, processId)
                && IsMenuWindow(cursorRoot);
            var isModal = cursorIsMenu == false
                && IsVisualStudioModal(
                    foregroundRoot,
                    visualStudioMainWindow,
                    processId);
            var isPopup = cursorIsMenu
                || (isModal == false
                    && IsVisualStudioPopup(cursorRoot, foregroundRoot, processId));
            var candidate = cursorIsMenu
                ? cursorRoot
                : isModal
                    ? foregroundRoot
                    : isPopup
                        ? cursorRoot
                        : IntPtr.Zero;

            if (candidate == IntPtr.Zero)
            {
                return false;
            }

            if (isPopup && TryGetMenuContentBounds(candidate, cursor, out var menuBounds))
            {
                Rectangle? topLevelBounds = null;
                var topLevelRegions = new List<Rectangle>();
                if (AddAutomationBounds(processId, ref topLevelBounds, topLevelRegions))
                {
                    for (var index = 0; index < topLevelRegions.Count; index++)
                    {
                        AddBounds(topLevelRegions[index], ref captureBounds, captureRegions);
                    }
                }
                else
                {
                    AddBounds(menuBounds, ref captureBounds, captureRegions);
                }

                return true;
            }

            if (TryGetWindowBounds(candidate, out var bounds) == false)
            {
                return false;
            }

            AddBounds(bounds, ref captureBounds, captureRegions);
            return true;
        }

        private static bool TryGetMenuContentBounds(
            IntPtr window,
            NativePoint cursor,
            out Rectangle bounds)
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(cursor.X, cursor.Y));
            var walker = TreeWalker.ControlViewWalker;

            while (element is not null)
            {
                if (TryGetMenuItemUnion(element, out bounds))
                {
                    return true;
                }

                element = walker.GetParent(element);
            }

            var root = AutomationElement.FromHandle(window);
            return TryGetMenuItemUnion(root, out bounds);
        }

        private static bool TryGetMenuItemUnion(AutomationElement root, out Rectangle bounds)
        {
            var condition = new AndCondition(
                new PropertyCondition(AutomationElement.IsOffscreenProperty, false),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem));
            var items = root.FindAll(TreeScope.Element | TreeScope.Descendants, condition);
            Rectangle? itemUnion = null;

            for (var index = 0; index < items.Count; index++)
            {
                var itemBounds = ToRectangle(items[index].Current.BoundingRectangle);
                if (itemBounds.IsEmpty == false)
                {
                    AddBounds(itemBounds, ref itemUnion);
                }
            }

            if (items.Count > 1 && itemUnion is Rectangle menuBounds)
            {
                bounds = menuBounds;
                return true;
            }

            bounds = Rectangle.Empty;
            return false;
        }

        private static bool IsVisualStudioPopup(IntPtr window, IntPtr foregroundRoot, int processId)
        {
            if (window == IntPtr.Zero || IsVisualStudioWindow(window, processId) == false)
            {
                return false;
            }

            return window != foregroundRoot
                || GetWindow(window, GetWindowOwner) != IntPtr.Zero
                || string.Equals(GetWindowClassName(window), NativeMenuWindowClass, StringComparison.Ordinal);
        }

        private static bool IsVisualStudioModal(
            IntPtr window,
            IntPtr visualStudioMainWindow,
            int processId)
        {
            if (window == IntPtr.Zero
                || IsVisualStudioWindow(window, processId) == false
                || IsMenuWindow(window))
            {
                return false;
            }

            var owner = GetWindow(window, GetWindowOwner);
            return (owner != IntPtr.Zero && IsWindowEnabled(owner) == false)
                || (visualStudioMainWindow != IntPtr.Zero
                    && IsWindowEnabled(visualStudioMainWindow) == false);
        }

        private static bool IsMenuWindow(IntPtr window)
        {
            if (string.Equals(
                GetWindowClassName(window),
                NativeMenuWindowClass,
                StringComparison.Ordinal))
            {
                return true;
            }

            var root = AutomationElement.FromHandle(window);
            return TryGetMenuItemUnion(root, out _);
        }

        private static bool IsVisualStudioWindow(IntPtr window, int processId)
        {
            GetWindowThreadProcessId(window, out var windowProcessId);
            return windowProcessId == processId && IsWindowVisible(window);
        }

        private static bool TryGetWindowBounds(IntPtr window, out Rectangle bounds)
        {
            if (DwmGetWindowAttribute(
                window,
                ExtendedFrameBoundsAttribute,
                out var extendedBounds,
                Marshal.SizeOf(typeof(NativeRectangle))) == 0)
            {
                bounds = extendedBounds.ToRectangle();
                return bounds.Width > 0 && bounds.Height > 0;
            }

            if (GetWindowRect(window, out var windowBounds))
            {
                bounds = windowBounds.ToRectangle();
                return bounds.Width > 0 && bounds.Height > 0;
            }

            bounds = Rectangle.Empty;
            return false;
        }

        private static bool AddAutomationBounds(
            int processId,
            ref Rectangle? captureBounds,
            List<Rectangle> captureRegions)
        {
            var itemCondition = new AndCondition(
                new PropertyCondition(AutomationElement.ProcessIdProperty, processId),
                new PropertyCondition(AutomationElement.IsOffscreenProperty, false),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem));
            var items = AutomationElement.RootElement.FindAll(TreeScope.Descendants, itemCondition);
            var itemBounds = new List<Rectangle>();
            var seedBounds = new List<Rectangle>();

            for (var index = 0; index < items.Count; index++)
            {
                var element = items[index];
                var bounds = ToRectangle(element.Current.BoundingRectangle);
                if (bounds.IsEmpty)
                {
                    continue;
                }

                itemBounds.Add(bounds);
                if (IsExpanded(element) || element.Current.HasKeyboardFocus)
                {
                    seedBounds.Add(bounds);
                }
            }

            var topLevelItems = FindTopLevelItems(itemBounds);
            var menuSurfaces = BuildMenuSurfaces(itemBounds, topLevelItems);
            var activeHeader = FindActiveHeader(topLevelItems, seedBounds, menuSurfaces);
            if (activeHeader is Rectangle header)
            {
                AddBounds(header, ref captureBounds, captureRegions);
            }

            AddSeedSurfaces(menuSurfaces, seedBounds, ref captureBounds, captureRegions);
            if (GetCursorPos(out var cursor))
            {
                AddCursorSurface(menuSurfaces, cursor, ref captureBounds, captureRegions);
            }

            AddAdjacentSurfaces(menuSurfaces, ref captureBounds, captureRegions);
            return activeHeader is Rectangle;
        }

        private static bool IsExpanded(AutomationElement element)
        {
            return element.TryGetCurrentPattern(
                ExpandCollapsePattern.Pattern,
                out var pattern)
                && pattern is ExpandCollapsePattern expandCollapse
                && expandCollapse.Current.ExpandCollapseState == ExpandCollapseState.Expanded;
        }

        private static List<Rectangle> FindTopLevelItems(List<Rectangle> itemBounds)
        {
            var topLevelItems = new List<Rectangle>();
            var top = int.MaxValue;

            for (var index = 0; index < itemBounds.Count; index++)
            {
                top = System.Math.Min(top, itemBounds[index].Top);
            }

            for (var index = 0; index < itemBounds.Count; index++)
            {
                if (System.Math.Abs(itemBounds[index].Top - top) <= TopLevelRowTolerance)
                {
                    topLevelItems.Add(itemBounds[index]);
                }
            }

            return topLevelItems.Count > 1
                ? topLevelItems
                : new List<Rectangle>();
        }

        private static List<Rectangle> BuildMenuSurfaces(
            List<Rectangle> itemBounds,
            List<Rectangle> topLevelItems)
        {
            var surfaces = new List<Rectangle>();

            for (var itemIndex = 0; itemIndex < itemBounds.Count; itemIndex++)
            {
                var item = itemBounds[itemIndex];
                if (topLevelItems.Contains(item))
                {
                    continue;
                }

                var merged = false;
                for (var surfaceIndex = 0; surfaceIndex < surfaces.Count; surfaceIndex++)
                {
                    if (BelongsToSurface(item, surfaces[surfaceIndex]))
                    {
                        surfaces[surfaceIndex] = Rectangle.Union(surfaces[surfaceIndex], item);
                        merged = true;
                        break;
                    }
                }

                if (merged == false)
                {
                    surfaces.Add(item);
                }
            }

            MergeTouchingSurfaces(surfaces);
            return surfaces;
        }

        private static bool BelongsToSurface(Rectangle item, Rectangle surface)
        {
            var horizontalOverlap = System.Math.Min(item.Right, surface.Right)
                - System.Math.Max(item.Left, surface.Left);
            var minimumWidth = System.Math.Min(item.Width, surface.Width);
            var verticalGap = System.Math.Max(0,
                System.Math.Max(item.Top, surface.Top) - System.Math.Min(item.Bottom, surface.Bottom));
            return horizontalOverlap >= minimumWidth / 2
                && verticalGap <= MenuAdjacencyTolerance;
        }

        private static Rectangle? FindActiveHeader(
            List<Rectangle> topLevelItems,
            List<Rectangle> seedBounds,
            List<Rectangle> menuSurfaces)
        {
            Rectangle? activeSurface = null;
            for (var surfaceIndex = 0; surfaceIndex < menuSurfaces.Count; surfaceIndex++)
            {
                for (var seedIndex = 0; seedIndex < seedBounds.Count; seedIndex++)
                {
                    var candidate = menuSurfaces[surfaceIndex];
                    if (candidate.IntersectsWith(seedBounds[seedIndex])
                        && (activeSurface is not Rectangle current
                            || candidate.Top < current.Top))
                    {
                        activeSurface = candidate;
                        break;
                    }
                }
            }

            if (activeSurface is not Rectangle surface)
            {
                return null;
            }

            Rectangle? closestHeader = null;
            var closestDistance = int.MaxValue;
            for (var index = 0; index < topLevelItems.Count; index++)
            {
                var header = topLevelItems[index];
                var verticalGap = surface.Top - header.Bottom;
                if (verticalGap < -TopLevelRowTolerance
                    || verticalGap > MenuAdjacencyTolerance)
                {
                    continue;
                }

                var distance = System.Math.Abs(header.Left - surface.Left);
                if (distance < closestDistance)
                {
                    closestHeader = header;
                    closestDistance = distance;
                }
            }

            return closestHeader;
        }

        private static void AddSeedSurfaces(
            List<Rectangle> surfaces,
            List<Rectangle> seedBounds,
            ref Rectangle? captureBounds,
            List<Rectangle> captureRegions)
        {
            for (var surfaceIndex = surfaces.Count - 1; surfaceIndex >= 0; surfaceIndex--)
            {
                for (var seedIndex = 0; seedIndex < seedBounds.Count; seedIndex++)
                {
                    if (surfaces[surfaceIndex].IntersectsWith(seedBounds[seedIndex]))
                    {
                        AddBounds(surfaces[surfaceIndex], ref captureBounds, captureRegions);
                        surfaces.RemoveAt(surfaceIndex);
                        break;
                    }
                }
            }
        }

        private static void AddCursorSurface(
            List<Rectangle> surfaces,
            NativePoint cursor,
            ref Rectangle? captureBounds,
            List<Rectangle> captureRegions)
        {
            for (var index = surfaces.Count - 1; index >= 0; index--)
            {
                if (surfaces[index].Contains(cursor.X, cursor.Y))
                {
                    AddBounds(surfaces[index], ref captureBounds, captureRegions);
                    surfaces.RemoveAt(index);
                    return;
                }
            }
        }

        private static void MergeTouchingSurfaces(List<Rectangle> surfaces)
        {
            var merged = true;
            while (merged)
            {
                merged = false;
                for (var first = 0; first < surfaces.Count && merged == false; first++)
                {
                    for (var second = first + 1; second < surfaces.Count; second++)
                    {
                        if (BelongsToSurface(surfaces[first], surfaces[second]))
                        {
                            surfaces[first] = Rectangle.Union(surfaces[first], surfaces[second]);
                            surfaces.RemoveAt(second);
                            merged = true;
                            break;
                        }
                    }
                }
            }
        }

        private static void AddAdjacentSurfaces(
            List<Rectangle> surfaces,
            ref Rectangle? captureBounds,
            List<Rectangle> captureRegions)
        {
            var addedSurface = true;
            while (addedSurface)
            {
                addedSurface = false;
                for (var index = surfaces.Count - 1; index >= 0; index--)
                {
                    if (IsAdjacentToCapture(surfaces[index], captureBounds) == false)
                    {
                        continue;
                    }

                    AddBounds(surfaces[index], ref captureBounds, captureRegions);
                    surfaces.RemoveAt(index);
                    addedSurface = true;
                }
            }
        }

        private static Rectangle ToRectangle(System.Windows.Rect bounds)
        {
            return bounds.IsEmpty
                ? Rectangle.Empty
                : Rectangle.FromLTRB(
                    (int)System.Math.Floor(bounds.Left),
                    (int)System.Math.Floor(bounds.Top),
                    (int)System.Math.Ceiling(bounds.Right),
                    (int)System.Math.Ceiling(bounds.Bottom));
        }

        private static bool IsAdjacentToCapture(Rectangle candidate, Rectangle? captureBounds)
        {
            if (captureBounds is not Rectangle current)
            {
                return false;
            }

            current.Inflate(MenuAdjacencyTolerance, MenuAdjacencyTolerance);
            return current.IntersectsWith(candidate);
        }

        private static void AddNativeMenuBounds(
            int processId,
            ref Rectangle? captureBounds,
            List<Rectangle> captureRegions)
        {
            var discoveredBounds = captureBounds;
            EnumWindows(
                (window, parameter) =>
                {
                    GetWindowThreadProcessId(window, out var windowProcessId);
                    if (windowProcessId != processId
                        || IsWindowVisible(window) == false
                        || string.Equals(GetWindowClassName(window), NativeMenuWindowClass, System.StringComparison.Ordinal) == false)
                    {
                        return true;
                    }

                    if (DwmGetWindowAttribute(
                        window,
                        ExtendedFrameBoundsAttribute,
                        out var extendedBounds,
                        Marshal.SizeOf(typeof(NativeRectangle))) == 0)
                    {
                        AddBounds(extendedBounds.ToRectangle(), ref discoveredBounds, captureRegions);
                    }
                    else if (GetWindowRect(window, out var bounds))
                    {
                        AddBounds(bounds.ToRectangle(), ref discoveredBounds, captureRegions);
                    }

                    return true;
                },
                IntPtr.Zero);
            captureBounds = discoveredBounds;
        }

        private static string GetWindowClassName(IntPtr window)
        {
            var className = new StringBuilder(256);
            _ = GetClassName(window, className, className.Capacity);
            return className.ToString();
        }

        private static void AddBounds(
            Rectangle bounds,
            ref Rectangle? captureBounds,
            List<Rectangle>? captureRegions = null)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            captureBounds = captureBounds is Rectangle current
                ? Rectangle.Union(current, bounds)
                : bounds;
            captureRegions?.Add(bounds);
        }

        private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr window, uint command);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr window, out NativeRectangle bounds);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowEnabled(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(NativePoint point);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(
            IntPtr window,
            int attribute,
            out NativeRectangle value,
            int valueSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            internal int X;
            internal int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;

            internal Rectangle ToRectangle()
            {
                return Rectangle.FromLTRB(Left, Top, Right, Bottom);
            }
        }
    }
}
