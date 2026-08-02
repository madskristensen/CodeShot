using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Text.Classification;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace CodeShot.ToolWindows
{
    internal static class FontCatalog
    {
        public const string FallbackFamily = "Consolas";
        public const double FallbackSize = 13;
        public const double MinimumSize = 6;
        public const double MaximumSize = 96;

        private static readonly object SyncRoot = new object();

        private static IReadOnlyList<string>? _families;
        private static Task<List<string>>? _monospaceFamiliesTask;

        public static IReadOnlyList<double> Sizes { get; } = new double[] { 8, 9, 10, 11, 12, 13, 14, 16, 18, 20, 24, 28, 32 };

        // Scanning the installed fonts builds a GlyphTypeface per family, which loads font files from
        // disk. Starting it early keeps that work off the UI thread, where the list is first needed by
        // the toolbar drop-down and the options page.
        public static void Prime()
        {
            _ = GetMonospaceFamiliesAsync();
        }

        public static IReadOnlyList<string> Families
        {
            get
            {
                if (_families is not null)
                {
                    return _families;
                }

                // The scan runs on the thread pool and never needs the main thread, so joining it here
                // cannot deadlock. It is normally already finished because it is primed at package load.
                var families = ThreadHelper.JoinableTaskFactory.Run(GetMonospaceFamiliesAsync).ToList();
                var editorFamily = GetEditorFont().family;

                if (!string.IsNullOrWhiteSpace(editorFamily) && !families.Contains(editorFamily, StringComparer.OrdinalIgnoreCase))
                {
                    families.Add(editorFamily);
                    families.Sort(StringComparer.OrdinalIgnoreCase);
                }

                if (families.Count == 0)
                {
                    families.Add(FallbackFamily);
                }

                _families = families;
                return _families;
            }
        }

        private static Task<List<string>> GetMonospaceFamiliesAsync()
        {
            lock (SyncRoot)
            {
                return _monospaceFamiliesTask ??= Task.Run(GetMonospaceFamilies);
            }
        }

        private static List<string> GetMonospaceFamilies()
        {
            var families = new List<string>();

            try
            {
                families.AddRange(Fonts.SystemFontFamilies
                    .Where(IsMonospace)
                    .Select(family => family.Source)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
            }

            return families;
        }

        public static (string family, double size) GetEditorFont()
        {
            var componentModel = Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel;
            var formatMapService = componentModel?.GetService<IClassificationFormatMapService>();
            var defaultProperties = formatMapService?.GetClassificationFormatMap("text")?.DefaultTextProperties;

            var family = defaultProperties?.TypefaceEmpty == false
                ? defaultProperties.Typeface.FontFamily.Source
                : FallbackFamily;
            var size = defaultProperties?.FontRenderingEmSizeEmpty == false
                ? defaultProperties.FontRenderingEmSize
                : FallbackSize;

            return (family, ClampSize(size));
        }

        public static string ResolveFamily(string? family)
            => Families.FirstOrDefault(candidate => string.Equals(candidate, family, StringComparison.OrdinalIgnoreCase))
                ?? Families.FirstOrDefault(candidate => string.Equals(candidate, FallbackFamily, StringComparison.OrdinalIgnoreCase))
                ?? Families[0];

        public static bool TryParseSize(string? text, out double size)
        {
            if (double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out size)
                || double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out size))
            {
                size = ClampSize(size);
                return true;
            }

            size = FallbackSize;
            return false;
        }

        public static string FormatSize(double size)
            => size.ToString("0.##", CultureInfo.CurrentCulture);

        public static double ClampSize(double size)
            => size <= 0 ? FallbackSize : Math.Min(MaximumSize, Math.Max(MinimumSize, size));

        private static bool IsMonospace(FontFamily fontFamily)
        {
            try
            {
                var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

                if (!typeface.TryGetGlyphTypeface(out var glyphTypeface))
                {
                    return false;
                }

                var narrow = GetAdvanceWidth(glyphTypeface, 'i');
                var wide = GetAdvanceWidth(glyphTypeface, 'W');

                return narrow > 0 && Math.Abs(narrow - wide) < 0.0001;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static double GetAdvanceWidth(GlyphTypeface glyphTypeface, char character)
            => glyphTypeface.CharacterToGlyphMap.TryGetValue(character, out var glyphIndex)
                && glyphTypeface.AdvanceWidths.TryGetValue(glyphIndex, out var width)
                ? width
                : 0;
    }
}
