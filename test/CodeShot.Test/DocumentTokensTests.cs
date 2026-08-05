using CodeShot.ToolWindows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeShot.Test
{
    [TestClass]
    public class DocumentTokensTests
    {
        [TestMethod]
        public void Constructor_CapturesDocumentValues()
        {
            var tokens = new DocumentTokens(@"C:\src\Widget.cs", "CSharp", "CodeShot");

            Assert.AreEqual(@"C:\src\Widget.cs", tokens.FilePath);
            Assert.AreEqual("Widget.cs", tokens.FileName);
            Assert.AreEqual("CSharp", tokens.Language);
            Assert.AreEqual("CodeShot", tokens.Workspace);
        }

        [TestMethod]
        public void Expand_ReplacesDocumentTokens()
        {
            var tokens = new DocumentTokens(@"C:\src\Widget.cs", "CSharp", "CodeShot");

            var expanded = tokens.Expand("{workspace} - {fileNameWithoutExtension}.{extension} ({language})");

            Assert.AreEqual("CodeShot - Widget.cs (CSharp)", expanded);
        }

        [TestMethod]
        public void ExpandOrDefault_EmptyDocumentUsesFallback()
        {
            var expanded = DocumentTokens.Empty.ExpandOrDefault("{fileName} - {workspace}", "CodeShot");

            Assert.AreEqual("CodeShot", expanded);
        }

        [TestMethod]
        public void ExpandFileName_ReplacesInvalidCharacters()
        {
            var tokens = new DocumentTokens(@"C:\src\Widget.cs", "CSharp", "CodeShot");

            var expanded = tokens.ExpandFileName("{filePath}", "CodeShot");

            Assert.AreEqual("C--src-Widget.cs", expanded);
        }

        [TestMethod]
        public void ExpandFileName_RemovesTrailingDotsAndSpaces()
        {
            var tokens = new DocumentTokens("Widget.cs", "CSharp", "CodeShot");

            var expanded = tokens.ExpandFileName("{fileName}... ", "CodeShot");

            Assert.AreEqual("Widget.cs", expanded);
        }
    }
}
