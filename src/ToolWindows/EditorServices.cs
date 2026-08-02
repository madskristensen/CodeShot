using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;

namespace CodeShot.ToolWindows
{
    // The preview resolves the same handful of MEF services on every refresh, so they are looked
    // up once instead of going through the global service provider each time.
    internal static class EditorServices
    {
        private static IComponentModel? _componentModel;
        private static IClassificationFormatMapService? _formatMaps;
        private static IViewClassifierAggregatorService? _classifiers;
        private static IVsEditorAdaptersFactoryService? _editorAdapters;
        private static ITextDocumentFactoryService? _textDocuments;

        public static IClassificationFormatMapService? FormatMaps
            => _formatMaps ??= GetService<IClassificationFormatMapService>();

        public static IViewClassifierAggregatorService? Classifiers
            => _classifiers ??= GetService<IViewClassifierAggregatorService>();

        public static IVsEditorAdaptersFactoryService? EditorAdapters
            => _editorAdapters ??= GetService<IVsEditorAdaptersFactoryService>();

        public static ITextDocumentFactoryService? TextDocuments
            => _textDocuments ??= GetService<ITextDocumentFactoryService>();

        private static T? GetService<T>() where T : class
        {
            _componentModel ??= Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel;
            return _componentModel?.GetService<T>();
        }
    }
}
