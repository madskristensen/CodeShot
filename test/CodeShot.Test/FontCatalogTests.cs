using System.Globalization;
using CodeShot.ToolWindows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeShot.Test
{
    [TestClass]
    [DoNotParallelize]
    public class FontCatalogTests
    {
        [DataTestMethod]
        [DataRow(double.NaN)]
        [DataRow(double.PositiveInfinity)]
        [DataRow(double.NegativeInfinity)]
        [DataRow(0d)]
        [DataRow(-1d)]
        public void ClampSize_InvalidValuesUseFallback(double value)
        {
            Assert.AreEqual(FontCatalog.FallbackSize, FontCatalog.ClampSize(value));
        }

        [DataTestMethod]
        [DataRow(1d, FontCatalog.MinimumSize)]
        [DataRow(FontCatalog.MinimumSize, FontCatalog.MinimumSize)]
        [DataRow(13.5d, 13.5d)]
        [DataRow(FontCatalog.MaximumSize, FontCatalog.MaximumSize)]
        [DataRow(1000d, FontCatalog.MaximumSize)]
        public void ClampSize_ConstrainsFiniteValues(double value, double expected)
        {
            Assert.AreEqual(expected, FontCatalog.ClampSize(value));
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("not a number")]
        [DataRow("NaN")]
        [DataRow("Infinity")]
        public void TryParseSize_InvalidTextUsesFallback(string? text)
        {
            var parsed = FontCatalog.TryParseSize(text, out var size);

            Assert.IsFalse(parsed);
            Assert.AreEqual(FontCatalog.FallbackSize, size);
        }

        [TestMethod]
        public void TryParseSize_AcceptsCurrentAndInvariantCultures()
        {
            var originalCulture = CultureInfo.CurrentCulture;

            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

                Assert.IsTrue(FontCatalog.TryParseSize("12,5", out var currentCultureSize));
                Assert.IsTrue(FontCatalog.TryParseSize("14.5", out var invariantCultureSize));
                Assert.AreEqual(12.5, currentCultureSize);
                Assert.AreEqual(14.5, invariantCultureSize);
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [TestMethod]
        public void TryParseSize_TrimsAndClampsParsedValue()
        {
            Assert.IsTrue(FontCatalog.TryParseSize(" 120 ", out var size));
            Assert.AreEqual(FontCatalog.MaximumSize, size);
        }

        [TestMethod]
        public void FormatSize_UsesCurrentCultureAndClampsValue()
        {
            var originalCulture = CultureInfo.CurrentCulture;

            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

                Assert.AreEqual("12,5", FontCatalog.FormatSize(12.5));
                Assert.AreEqual("96", FontCatalog.FormatSize(120));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }
    }
}
