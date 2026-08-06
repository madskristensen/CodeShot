using CodeShot.ToolWindows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeShot.Test
{
    [TestClass]
    public class TextWrapIndentTests
    {
        [DataTestMethod]
        [DataRow("Call();", 0, "")]
        [DataRow("    Call();", 4, "    ")]
        [DataRow("\tCall();", 1, "    ")]
        [DataRow(" \tCall();", 2, "    ")]
        [DataRow("     \tCall();", 6, "        ")]
        public void Split_ReturnsLeadingCharactersAndEquivalentSpaces(string line, int characterCount, string whitespace)
        {
            var indent = TextWrapIndent.Split(line);

            Assert.AreEqual(characterCount, indent.CharacterCount);
            Assert.AreEqual(whitespace, indent.Whitespace);
        }
    }
}
