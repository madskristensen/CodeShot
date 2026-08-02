using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Microsoft.VisualStudio.Imaging;

namespace CodeShot.ToolWindows
{
    internal sealed class CodeShotToolWindow : BaseToolWindow<CodeShotToolWindow>
    {
        public override string GetTitle(int toolWindowId) => Vsix.Name;

        public override Type PaneType => typeof(Pane);

        public override async System.Threading.Tasks.Task<FrameworkElement> CreateAsync(int toolWindowId, CancellationToken cancellationToken)
        {
            // Reading the settings and finishing the font scan before the control exists means the
            // preview is built with the real font straight away, instead of rendering with the XAML
            // defaults and then visibly re-laying itself out once the settings have loaded.
            General options = await General.GetLiveInstanceAsync();
            await FontCatalog.PrimeAsync();

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            return new CodeShotToolWindowControl(options);
        }

        // Visual Studio persists the window layout against this GUID, so it has to stay exactly as it
        // was on the ToolWindowPane that this pane replaced.
        [Guid("ce9b4700-7154-4196-9957-858230f19734")]
        internal sealed class Pane : ToolWindowPane
        {
            public Pane()
            {
                BitmapImageMoniker = KnownMonikers.Camera;
                ToolBar = new CommandID(PackageGuids.CodeShot, PackageIds.CodeShotToolbar);
            }
        }
    }
}
