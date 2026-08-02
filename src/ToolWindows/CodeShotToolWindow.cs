using Microsoft.VisualStudio.Shell;

namespace CodeShot.ToolWindows
{
    internal sealed class CodeShotToolWindow : ToolWindowPane
    {
        public CodeShotToolWindow()
            : base(null)
        {
            Caption = "CodeShot";
            Content = new CodeShotToolWindowControl();
        }
    }
}
