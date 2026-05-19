using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using Hush.VS.Options;

namespace Hush.VS.Classification;

[Export(typeof(IClassifierProvider))]
[ContentType(Constants.ContentTypeCSharp)]
internal sealed class HushClassifierProvider : IClassifierProvider
{
    [Import] internal IClassificationTypeRegistryService Registry = null!;
    [Import] internal MuteStateService State = null!;

    public IClassifier GetClassifier(ITextBuffer textBuffer) =>
        textBuffer.Properties.GetOrCreateSingletonProperty(
            () => new HushClassifier(textBuffer, Registry, State));
}
