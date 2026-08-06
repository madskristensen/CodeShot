using System;
using System.Collections.Generic;

namespace CodeShot.ToolWindows
{
    internal static class SelectionTextProcessor
    {
        internal static IReadOnlyList<TextSelectionSegment> Normalize(
            IReadOnlyList<SelectedLinePart> lines,
            bool keepOriginalIndentation,
            Func<int, char> getCharacter)
        {
            var indentation = keepOriginalIndentation ? 0 : GetCommonIndentation(lines, getCharacter);
            var segments = new List<TextSelectionSegment>(lines.Count);

            foreach (var line in lines)
            {
                var start = Math.Min(Math.Max(line.Start, line.LineStart + indentation), line.End);
                segments.Add(new TextSelectionSegment(start, line.End - start));
            }

            if (segments.Count > 1 && segments[segments.Count - 1].Length == 0)
            {
                segments.RemoveAt(segments.Count - 1);
            }

            return segments;
        }

        private static int GetCommonIndentation(
            IReadOnlyList<SelectedLinePart> lines,
            Func<int, char> getCharacter)
        {
            var indentation = int.MaxValue;

            foreach (var line in lines)
            {
                var contentStart = line.Start;

                while (contentStart < line.End && char.IsWhiteSpace(getCharacter(contentStart)))
                {
                    contentStart++;
                }

                if (contentStart == line.End)
                {
                    continue;
                }

                indentation = Math.Min(indentation, contentStart - line.LineStart);
            }

            return indentation == int.MaxValue ? 0 : indentation;
        }
    }

    internal readonly struct SelectedLinePart
    {
        internal SelectedLinePart(int lineStart, int start, int end)
        {
            LineStart = lineStart;
            Start = start;
            End = end;
        }

        internal int LineStart { get; }
        internal int Start { get; }
        internal int End { get; }
    }

    internal readonly struct TextSelectionSegment
    {
        internal TextSelectionSegment(int start, int length)
        {
            Start = start;
            Length = length;
        }

        internal int Start { get; }
        internal int Length { get; }
    }
}
