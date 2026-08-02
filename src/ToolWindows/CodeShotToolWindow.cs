using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Imaging;

namespace CodeShot.ToolWindows
{
    [Guid("ce9b4700-7154-4196-9957-858230f19734")]
    internal sealed class CodeShotToolWindow : ToolWindowPane
    {
        public CodeShotToolWindow()
            : base(null)
        {
            Caption = Vsix.Name;
            BitmapImageMoniker = KnownMonikers.Camera;
            ToolBar = new CommandID(PackageGuids.CodeShot, PackageIds.CodeShotToolbar);
            Content = new CodeShotToolWindowControl();
        }
    }
}
