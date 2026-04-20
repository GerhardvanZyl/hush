using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using MutedBoilerplate.VS.Options;

namespace MutedBoilerplate.VS.Classification;

[Export(typeof(IClassifierProvider))]
[ContentType(Constants.ContentTypeCSharp)]
internal sealed class MutedClassifierProvider : IClassifierProvider
{
    [Import] internal IClassificationTypeRegistryService Registry = null!;
    [Import] internal MuteStateService State = null!;

    public IClassifier GetClassifier(ITextBuffer textBuffer) =>
        textBuffer.Properties.GetOrCreateSingletonProperty(
            () => new MutedClassifier(textBuffer, Registry, State));
}
