using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using MutedBoilerplate.VS.Options;

namespace MutedBoilerplate.VS.Outlining;

[Export(typeof(ITaggerProvider))]
[ContentType(Constants.ContentTypeCSharp)]
[TagType(typeof(IOutliningRegionTag))]
internal sealed class MutedOutliningTaggerProvider : ITaggerProvider
{
    [Import] internal MuteStateService State = null!;

    public ITagger<T>? CreateTagger<T>(ITextBuffer buffer) where T : ITag
    {
        var tagger = buffer.Properties.GetOrCreateSingletonProperty(
            () => new MutedOutliningTagger(buffer, State));
        return tagger as ITagger<T>;
    }
}
