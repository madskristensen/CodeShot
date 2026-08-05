using System.Windows;
using CodeShot.ToolWindows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeShot.Test
{
    [TestClass]
    public class CodeAnnotationTests
    {
        [TestMethod]
        public void Bounds_NormalizesReversedPoints()
        {
            var annotation = new CodeAnnotation(
                AnnotationKind.Rectangle,
                new Point(120, 80),
                new Point(20, 10));

            Assert.AreEqual(new Rect(20, 10, 100, 70), annotation.Bounds);
        }

        [TestMethod]
        public void Bounds_PreservesZeroSizedAnnotation()
        {
            var annotation = new CodeAnnotation(
                AnnotationKind.Text,
                new Point(12, 34),
                new Point(12, 34),
                "Note");

            Assert.AreEqual(new Rect(12, 34, 0, 0), annotation.Bounds);
            Assert.AreEqual("Note", annotation.Text);
        }

        [TestMethod]
        public void RectConstructor_UsesOppositeCorners()
        {
            var annotation = new CodeAnnotation(AnnotationKind.Highlight, new Rect(5, 10, 30, 40));

            Assert.AreEqual(new Point(5, 10), annotation.Start);
            Assert.AreEqual(new Point(35, 50), annotation.End);
        }
    }
}
