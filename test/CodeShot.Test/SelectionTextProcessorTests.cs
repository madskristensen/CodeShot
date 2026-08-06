using System.Collections.Generic;
using System.Linq;
using CodeShot.ToolWindows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeShot.Test
{
    [TestClass]
    public class SelectionTextProcessorTests
    {
        [TestMethod]
        public void Normalize_RemovesCommonIndentation()
        {
            var segments = Normalize(
                keepOriginalIndentation: false,
                (0, 0, "    class Widget"),
                (18, 18, "    {"),
                (25, 25, "        void Run()"));

            CollectionAssert.AreEqual(new[] { 4, 22, 29 }, segments.Select(segment => segment.Start).ToArray());
            CollectionAssert.AreEqual(new[] { 12, 1, 14 }, segments.Select(segment => segment.Length).ToArray());
        }

        [TestMethod]
        public void Normalize_PreservesRelativeIndentation()
        {
            var segments = Normalize(
                keepOriginalIndentation: false,
                (0, 0, "  if (ready)"),
                (14, 14, "      Run();"));

            Assert.AreEqual(2, segments[0].Start);
            Assert.AreEqual(16, segments[1].Start);
            Assert.AreEqual(10, segments[1].Length);
        }

        [TestMethod]
        public void Normalize_PreservesOriginalIndentationWhenRequested()
        {
            var segments = Normalize(
                keepOriginalIndentation: true,
                (0, 0, "    class Widget"),
                (18, 18, "        void Run()"));

            CollectionAssert.AreEqual(new[] { 0, 18 }, segments.Select(segment => segment.Start).ToArray());
            CollectionAssert.AreEqual(new[] { 16, 18 }, segments.Select(segment => segment.Length).ToArray());
        }

        [TestMethod]
        public void Normalize_UsesPartialFirstLineToDetermineIndentation()
        {
            var segments = Normalize(
                keepOriginalIndentation: false,
                (0, 6, "Widget"),
                (14, 14, "        Run();"));

            Assert.AreEqual(6, segments[0].Start);
            Assert.AreEqual(20, segments[1].Start);
        }

        [TestMethod]
        public void Normalize_TreatsTabsAsIndentationCharacters()
        {
            var segments = Normalize(
                keepOriginalIndentation: false,
                (0, 0, "\tclass Widget"),
                (15, 15, "\t\tRun();"));

            Assert.AreEqual(1, segments[0].Start);
            Assert.AreEqual(16, segments[1].Start);
            Assert.AreEqual(7, segments[1].Length);
        }

        [TestMethod]
        public void Normalize_IgnoresBlankLinesWhenMeasuringIndentation()
        {
            var segments = Normalize(
                keepOriginalIndentation: false,
                (0, 0, "        "),
                (10, 10, "    Run();"));

            Assert.AreEqual(4, segments[0].Start);
            Assert.AreEqual(4, segments[0].Length);
            Assert.AreEqual(14, segments[1].Start);
        }

        [TestMethod]
        public void Normalize_RemovesTrailingEmptyLine()
        {
            var segments = Normalize(
                keepOriginalIndentation: false,
                (0, 0, "Run();"),
                (8, 8, string.Empty));

            Assert.AreEqual(1, segments.Count);
        }

        private static IReadOnlyList<TextSelectionSegment> Normalize(
            bool keepOriginalIndentation,
            params (int lineStart, int start, string text)[] sourceLines)
        {
            var characters = new Dictionary<int, char>();
            var lines = new List<SelectedLinePart>(sourceLines.Length);

            foreach (var sourceLine in sourceLines)
            {
                for (var index = 0; index < sourceLine.text.Length; index++)
                {
                    characters[sourceLine.start + index] = sourceLine.text[index];
                }

                lines.Add(new SelectedLinePart(
                    sourceLine.lineStart,
                    sourceLine.start,
                    sourceLine.start + sourceLine.text.Length));
            }

            return SelectionTextProcessor.Normalize(lines, keepOriginalIndentation, position => characters[position]);
        }
    }
}
