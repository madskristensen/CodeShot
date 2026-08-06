using CodeShot.ToolWindows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeShot.Test
{
    [TestClass]
    public class TextCaptureWidthTests
    {
        [DataTestMethod]
        [DataRow(0)]
        [DataRow(319)]
        [DataRow(double.NaN)]
        [DataRow(double.PositiveInfinity)]
        [DataRow(double.NegativeInfinity)]
        public void Clamp_ReturnsMinimumForUnsupportedWidths(double value)
        {
            Assert.AreEqual(TextCaptureWidth.Minimum, TextCaptureWidth.Clamp(value));
        }

        [DataTestMethod]
        [DataRow(320)]
        [DataRow(640)]
        [DataRow(1234.5)]
        public void Clamp_PreservesSupportedWidths(double value)
        {
            Assert.AreEqual(value, TextCaptureWidth.Clamp(value));
        }

        [TestMethod]
        public void AddDelta_AccumulatesFromPendingWidth()
        {
            Assert.AreEqual(625, TextCaptureWidth.AddDelta(600, 500, 25));
        }

        [TestMethod]
        public void AddDelta_UsesCurrentWidthWithoutPendingUpdate()
        {
            Assert.AreEqual(525, TextCaptureWidth.AddDelta(null, 500, 25));
        }

        [TestMethod]
        public void AddDelta_ClampsAccumulatedWidth()
        {
            Assert.AreEqual(TextCaptureWidth.Minimum, TextCaptureWidth.AddDelta(330, 500, -25));
        }
    }
}
