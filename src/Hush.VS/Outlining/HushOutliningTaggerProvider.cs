using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using Hush.VS.Options;

namespace Hush.VS.Outlining;

[Export(typeof(ITaggerProvider))]
[ContentType(Constants.ContentTypeCSharp)]
[TagType(typeof(IOutliningRegionTag))]
internal sealed class HushOutliningTaggerProvider : ITaggerProvider
{
    [Import] internal MuteStateService State = null!;

    public ITagger<T>? CreateTagger<T>(ITextBuffer buffer) where T : ITag
    {
        var tagger = buffer.Properties.GetOrCreateSingletonProperty(
            () => new HushOutliningTagger(buffer, State));
        return tagger as ITagger<T>;
    }
}
