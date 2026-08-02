using CodeShot.ToolWindows;
using System.ComponentModel.Design;
using System.Linq;
using System.Runtime.InteropServices;

namespace CodeShot.Commands
{
    // Toolbar combo boxes are not covered by BaseCommand<T> because they exchange values
    // with the shell through the InValue and OutValue members of OleMenuCmdEventArgs.
    internal static class FontComboCommands
    {
        public static void Register(OleMenuCommandService commandService)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            AddCommand(commandService, PackageIds.FontFamilyCombo, OnFontFamily, gateOnToolWindow: true);
            AddCommand(commandService, PackageIds.FontFamilyComboList, OnFontFamilyList, gateOnToolWindow: false);
            AddCommand(commandService, PackageIds.FontSizeCombo, OnFontSize, gateOnToolWindow: true);
            AddCommand(commandService, PackageIds.FontSizeComboList, OnFontSizeList, gateOnToolWindow: false);
        }

        private static void AddCommand(OleMenuCommandService commandService, int id, EventHandler handler, bool gateOnToolWindow)
        {
            var command = new OleMenuCommand(handler, new CommandID(PackageGuids.CodeShot, id));

            if (gateOnToolWindow)
            {
                command.BeforeQueryStatus += OnBeforeQueryStatus;
            }

            commandService.AddCommand(command);
        }

        private static void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            if (sender is OleMenuCommand command)
            {
                command.Enabled = CodeShotToolWindowControl.Current is not null;
            }
        }

        private static void OnFontFamily(object sender, EventArgs e)
        {
            try
            {
                if (e is not OleMenuCmdEventArgs args)
                {
                    return;
                }

                var control = CodeShotToolWindowControl.Current;

                if (args.OutValue != IntPtr.Zero)
                {
                    var current = control?.PreviewFontFamily ?? FontCatalog.ResolveFamily(null);
                    Marshal.GetNativeVariantForObject(current, args.OutValue);
                }
                else if (control is not null && args.InValue?.ToString() is string family && !string.IsNullOrWhiteSpace(family))
                {
                    control.PreviewFontFamily = family;
                }
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
            }
        }

        private static void OnFontFamilyList(object sender, EventArgs e)
        {
            try
            {
                if (e is OleMenuCmdEventArgs args && args.OutValue != IntPtr.Zero)
                {
                    Marshal.GetNativeVariantForObject(FontCatalog.Families.ToArray(), args.OutValue);
                }
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
            }
        }

        private static void OnFontSize(object sender, EventArgs e)
        {
            try
            {
                if (e is not OleMenuCmdEventArgs args)
                {
                    return;
                }

                var control = CodeShotToolWindowControl.Current;

                if (args.OutValue != IntPtr.Zero)
                {
                    var current = FontCatalog.FormatSize(control?.PreviewFontSize ?? FontCatalog.FallbackSize);
                    Marshal.GetNativeVariantForObject(current, args.OutValue);
                }
                else if (control is not null && FontCatalog.TryParseSize(args.InValue?.ToString(), out var size))
                {
                    control.PreviewFontSize = size;
                }
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
            }
        }

        private static void OnFontSizeList(object sender, EventArgs e)
        {
            try
            {
                if (e is OleMenuCmdEventArgs args && args.OutValue != IntPtr.Zero)
                {
                    var sizes = FontCatalog.Sizes.Select(FontCatalog.FormatSize).ToArray();
                    Marshal.GetNativeVariantForObject(sizes, args.OutValue);
                }
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
            }
        }
    }
}
