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
        private static readonly IReadOnlyList<string> FallbackFamilies = new[] { FallbackFamily };

        private static volatile IReadOnlyList<string>? _families;
        private static Task<List<string>>? _monospaceFamiliesTask;

        public static IReadOnlyList<double> Sizes { get; } = new double[] { 8, 9, 10, 11, 12, 13, 14, 16, 18, 20, 24, 28, 32 };

        // Scanning the installed fonts builds a GlyphTypeface per family and loads font files from
        // disk. Until the background scan completes, synchronous UI callers receive the fallback
        // immediately instead of joining the scan and freezing Visual Studio.
        public static async Task PrimeAsync()
        {
            var families = await GetMonospaceFamiliesAsync();
            _families = families.Count > 0 ? families : FallbackFamilies;
        }

        public static IReadOnlyList<string> Families
        {
            get
            {
                var families = _families ?? FallbackFamilies;
                var editorFamily = GetEditorFont().family;

                if (string.IsNullOrWhiteSpace(editorFamily)
                    || families.Contains(editorFamily, StringComparer.OrdinalIgnoreCase))
                {
                    return families;
                }

                var combined = families.ToList();
                combined.Add(editorFamily);
                combined.Sort(StringComparer.OrdinalIgnoreCase);
                return combined;
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
            var defaultProperties = EditorServices.FormatMaps?.GetClassificationFormatMap("text")?.DefaultTextProperties;

            var family = defaultProperties?.TypefaceEmpty == false
                ? defaultProperties.Typeface.FontFamily.Source
                : FallbackFamily;
            var size = defaultProperties?.FontRenderingEmSizeEmpty == false
                ? defaultProperties.FontRenderingEmSize
                : FallbackSize;

            return (family, ClampSize(size));
        }

        public static string ResolveFamily(string? family)
        {
            var families = Families;
            var match = family is null
                ? null
                : families.FirstOrDefault(name => string.Equals(name, family, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return match;
            }

            return families.FirstOrDefault(name => string.Equals(name, FallbackFamily, StringComparison.OrdinalIgnoreCase))
                ?? families[0];
        }

        public static bool TryParseSize(string? text, out double size)
        {
            var trimmed = text?.Trim();

            if ((double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out size)
                    || double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out size))
                && IsFinite(size))
            {
                size = ClampSize(size);
                return true;
            }

            size = FallbackSize;
            return false;
        }

        public static string FormatSize(double size)
            => ClampSize(size).ToString("0.##", CultureInfo.CurrentCulture);

        public static double ClampSize(double size)
            => IsFinite(size) == false || size <= 0
                ? FallbackSize
                : Math.Min(MaximumSize, Math.Max(MinimumSize, size));

        private static bool IsFinite(double value)
            => double.IsNaN(value) == false && double.IsInfinity(value) == false;

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
            catch (Exception ex)
            {
                _ = ex.LogAsync();
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
