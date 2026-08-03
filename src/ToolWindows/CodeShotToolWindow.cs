using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell.Interop;

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
        // was on the ToolWindowPane that this pane replaced. It doubles as the key binding scope, which
        // is what makes Ctrl+C and Ctrl+S reach the copy and save commands only while this window is
        // the active one.
        [Guid(PackageGuids.CodeShotToolWindowString)]
        internal sealed class Pane : ToolWindowPane
        {
            public Pane()
            {
                BitmapImageMoniker = KnownMonikers.Camera;
                ToolBar = new CommandID(PackageGuids.CodeShot, PackageIds.CodeShotToolbar);
            }

            public override void OnToolWindowCreated()
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                base.OnToolWindowCreated();

                // The shell already derives the command UI context from the persistence GUID, but the
                // key bindings silently do nothing if it ever does not, so set it explicitly.
                Guid commandUI = PackageGuids.CodeShotToolWindow;
                ((IVsWindowFrame)Frame).SetGuidProperty((int)__VSFPROPID.VSFPROPID_CmdUIGuid, ref commandUI);
            }
        }
    }
}
