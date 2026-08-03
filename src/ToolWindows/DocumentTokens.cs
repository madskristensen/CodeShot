using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CodeShot.ToolWindows
{
    // The window title and the save file name expand the same placeholders, but the document they
    // describe is only reachable while the selection is being read. The values are captured once per
    // refresh so that saving an image later still names it after the code that is in the picture.
    internal sealed class DocumentTokens
    {
        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

        public DocumentTokens(string? filePath, string? language, string? workspace)
        {
            FilePath = filePath ?? string.Empty;
            Language = language ?? string.Empty;
            Workspace = workspace ?? string.Empty;
            FileName = FilePath.Length == 0 ? string.Empty : Path.GetFileName(FilePath);
        }

        public static DocumentTokens Empty { get; } = new DocumentTokens(null, null, null);

        public string FilePath { get; }

        public string FileName { get; }

        public string Language { get; }

        public string Workspace { get; }

        public string Expand(string template)
        {
            if (string.IsNullOrEmpty(template))
            {
                return string.Empty;
            }

            var now = DateTime.Now;

            return template
                .Replace("{fileName}", FileName)
                .Replace("{fileNameWithoutExtension}", FileName.Length == 0 ? string.Empty : Path.GetFileNameWithoutExtension(FileName))
                .Replace("{filePath}", FilePath)
                .Replace("{extension}", FileName.Length == 0 ? string.Empty : Path.GetExtension(FileName).TrimStart('.'))
                .Replace("{language}", Language)
                .Replace("{workspace}", Workspace)
                .Replace("{date}", now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Replace("{time}", now.ToString("HHmmss", CultureInfo.InvariantCulture));
        }

        // A template made only of tokens collapses to separators when the document behind them has
        // no name, so anything that carries no text at all falls back to the caller's default.
        public string ExpandOrDefault(string template, string fallback)
        {
            var expanded = Expand(template);
            return expanded.Any(char.IsLetterOrDigit) ? expanded.Trim() : fallback;
        }

        // Tokens such as {filePath} expand to text that cannot be part of a file name, and the user
        // is free to type anything into the template as well, so the result is always sanitized.
        public string ExpandFileName(string template, string fallback)
        {
            var expanded = ExpandOrDefault(template, fallback);
            var builder = new System.Text.StringBuilder(expanded.Length);

            foreach (var character in expanded)
            {
                builder.Append(Array.IndexOf(InvalidFileNameChars, character) >= 0 ? '-' : character);
            }

            // Trailing dots and spaces are legal to write but cannot be opened again on Windows.
            var sanitized = builder.ToString().Trim().TrimEnd('.', ' ');
            return sanitized.Length == 0 ? fallback : sanitized;
        }
    }
}
