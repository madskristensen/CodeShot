using CodeShot.ToolWindows;
using System.Linq;
using System.Runtime.InteropServices;

namespace CodeShot.Commands
{
    // Toolbar combo boxes exchange values with the shell through the InValue and OutValue members
    // of OleMenuCmdEventArgs, so Execute is handled synchronously instead of through ExecuteAsync.
    internal abstract class BaseComboCommand<T> : BaseCommand<T> where T : class, new()
    {
        protected virtual bool RequiresToolWindow => true;

        protected abstract object? GetValue();

        protected virtual void SetValue(string value)
        {
        }

        protected override void BeforeQueryStatus(EventArgs e)
        {
            if (RequiresToolWindow)
            {
                Command.Enabled = CodeShotToolWindowControl.Current?.SupportsCodeFormatting == true;
            }
        }

        protected override void Execute(object sender, EventArgs e)
        {
            try
            {
                if (e is not OleMenuCmdEventArgs args)
                {
                    return;
                }

                if (args.OutValue != IntPtr.Zero)
                {
                    if (GetValue() is object value)
                    {
                        Marshal.GetNativeVariantForObject(value, args.OutValue);
                    }
                }
                else if (args.InValue?.ToString() is string input && !string.IsNullOrWhiteSpace(input))
                {
                    SetValue(input);
                }
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
            }
        }
    }

    [Command(PackageIds.FontFamilyCombo)]
    internal sealed class FontFamilyComboCommand : BaseComboCommand<FontFamilyComboCommand>
    {
        protected override object? GetValue()
            => CodeShotToolWindowControl.Current?.PreviewFontFamily ?? FontCatalog.ResolveFamily(null);

        protected override void SetValue(string value)
        {
            if (CodeShotToolWindowControl.Current is CodeShotToolWindowControl control)
            {
                control.PreviewFontFamily = value;
            }
        }
    }

    [Command(PackageIds.FontFamilyComboList)]
    internal sealed class FontFamilyComboListCommand : BaseComboCommand<FontFamilyComboListCommand>
    {
        // The list provider must stay enabled or the drop-down cannot be populated.
        protected override bool RequiresToolWindow => false;

        protected override object? GetValue() => FontCatalog.Families.ToArray();
    }

    [Command(PackageIds.FontSizeCombo)]
    internal sealed class FontSizeComboCommand : BaseComboCommand<FontSizeComboCommand>
    {
        protected override object? GetValue()
            => FontCatalog.FormatSize(CodeShotToolWindowControl.Current?.PreviewFontSize ?? FontCatalog.FallbackSize);

        protected override void SetValue(string value)
        {
            if (CodeShotToolWindowControl.Current is CodeShotToolWindowControl control && FontCatalog.TryParseSize(value, out var size))
            {
                control.PreviewFontSize = size;
            }
        }
    }

    [Command(PackageIds.FontSizeComboList)]
    internal sealed class FontSizeComboListCommand : BaseComboCommand<FontSizeComboListCommand>
    {
        // The list provider must stay enabled or the drop-down cannot be populated.
        protected override bool RequiresToolWindow => false;

        protected override object? GetValue() => FontCatalog.Sizes.Select(FontCatalog.FormatSize).ToArray();
    }
}
