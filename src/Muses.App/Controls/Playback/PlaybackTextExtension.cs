using Avalonia.Markup.Xaml;
using Muses.Core.L10n;

namespace Muses.App.Controls.Playback;

public sealed class PlaybackTextExtension(string english, string simplifiedChinese) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => L10n.Tr(english, simplifiedChinese);
}
