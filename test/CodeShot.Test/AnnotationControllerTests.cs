using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CodeShot.ToolWindows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeShot.Test
{
    [TestClass]
    public class AnnotationControllerTests
    {
        private AnnotationController _controller = null!;
        private List<string> _statuses = null!;

        [TestInitialize]
        public void Initialize()
        {
            _statuses = new List<string>();
            _controller = new AnnotationController(
                new Canvas { Width = 500, Height = 400 },
                new Canvas(),
                status => _statuses.Add(status));
        }

        [TestMethod]
        public void CreateCroppedState_TranslatesIntersectingBoxAnnotations()
        {
            Restore(
                new CodeAnnotation(AnnotationKind.Rectangle, new Point(80, 90), new Point(160, 170)),
                new CodeAnnotation(AnnotationKind.Highlight, new Point(120, 130), new Point(180, 160)),
                new CodeAnnotation(AnnotationKind.Redaction, new Point(290, 290), new Point(330, 330)));

            var state = _controller.CreateCroppedState(new Rect(100, 100, 200, 200));

            Assert.AreEqual(3, state.Annotations.Count);
            AssertAnnotation(state.Annotations[0], AnnotationKind.Rectangle, -20, -10, 60, 70);
            AssertAnnotation(state.Annotations[1], AnnotationKind.Highlight, 20, 30, 80, 60);
            AssertAnnotation(state.Annotations[2], AnnotationKind.Redaction, 190, 190, 230, 230);
        }

        [TestMethod]
        public void CreateCroppedState_ExcludesBoxesOutsideCrop()
        {
            Restore(
                new CodeAnnotation(AnnotationKind.Rectangle, new Point(0, 0), new Point(50, 50)),
                new CodeAnnotation(AnnotationKind.Redaction, new Point(301, 100), new Point(350, 150)));

            var state = _controller.CreateCroppedState(new Rect(100, 100, 200, 200));

            Assert.AreEqual(0, state.Annotations.Count);
        }

        [TestMethod]
        public void CreateCroppedState_IncludesContainedArrowAndPreservesText()
        {
            Restore(
                new CodeAnnotation(AnnotationKind.Arrow, new Point(130, 150), new Point(220, 150)),
                new CodeAnnotation(AnnotationKind.Text, new Point(140, 180), new Point(220, 220), "Review this"));

            var state = _controller.CreateCroppedState(new Rect(100, 100, 200, 200));

            Assert.AreEqual(2, state.Annotations.Count);
            AssertAnnotation(state.Annotations[0], AnnotationKind.Arrow, 30, 50, 120, 50);
            AssertAnnotation(state.Annotations[1], AnnotationKind.Text, 40, 80, 120, 120);
            Assert.AreEqual("Review this", state.Annotations[1].Text);
        }

        [TestMethod]
        public void CreateCroppedState_ExcludesPartiallyClippedArrowAndText()
        {
            Restore(
                new CodeAnnotation(AnnotationKind.Arrow, new Point(95, 150), new Point(180, 150)),
                new CodeAnnotation(AnnotationKind.Text, new Point(250, 250), new Point(320, 290), "Outside"));

            var state = _controller.CreateCroppedState(new Rect(100, 100, 200, 200));

            Assert.AreEqual(0, state.Annotations.Count);
        }

        [TestMethod]
        public void CaptureState_CopiesTheAnnotationCollection()
        {
            var annotations = new List<CodeAnnotation>
            {
                new CodeAnnotation(AnnotationKind.Rectangle, new Rect(10, 10, 20, 20)),
            };
            var state = new AnnotationController.State(annotations);

            annotations.Clear();

            Assert.AreEqual(1, state.Annotations.Count);
        }

        [TestMethod]
        public void HandleSurfaceSizeChanged_ClearsAnnotationsAndInvalidatesHistory()
        {
            Restore(new CodeAnnotation(AnnotationKind.Redaction, new Rect(10, 10, 20, 20)));
            var invalidated = false;
            _controller.HistoryInvalidated += () => invalidated = true;

            _controller.HandleSurfaceSizeChanged();

            Assert.IsTrue(invalidated);
            Assert.IsFalse(_controller.HasAnnotations);
            Assert.IsFalse(_controller.HasRedactions);
            Assert.AreEqual("Annotations cleared because the preview layout changed.", _statuses.Last());
        }

        private void Restore(params CodeAnnotation[] annotations)
            => _controller.RestoreState(new AnnotationController.State(annotations));

        private static void AssertAnnotation(
            CodeAnnotation annotation,
            AnnotationKind kind,
            double startX,
            double startY,
            double endX,
            double endY)
        {
            Assert.AreEqual(kind, annotation.Kind);
            Assert.AreEqual(new Point(startX, startY), annotation.Start);
            Assert.AreEqual(new Point(endX, endY), annotation.End);
        }
    }
}
