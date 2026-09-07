using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Muses.App.Services;

namespace Muses.App.Controls;

public partial class ArtworkControl : UserControl
{
    public static readonly StyledProperty<string?> SourceUrlProperty =
        AvaloniaProperty.Register<ArtworkControl, string?>(nameof(SourceUrl));

    public static readonly StyledProperty<double> GlyphSizeProperty =
        AvaloniaProperty.Register<ArtworkControl, double>(nameof(GlyphSize), 16.0);

    public string? SourceUrl
    {
        get => GetValue(SourceUrlProperty);
        set => SetValue(SourceUrlProperty, value);
    }

    public double GlyphSize
    {
        get => GetValue(GlyphSizeProperty);
        set => SetValue(GlyphSizeProperty, value);
    }

    private string? _activeUrl;

    public ArtworkControl()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SourceUrlProperty)
        {
            UpdateArtwork(change.GetNewValue<string?>());
        }
        else if (change.Property == CornerRadiusProperty)
        {
            Container.CornerRadius = change.GetNewValue<CornerRadius>();
        }
        else if (change.Property == GlyphSizeProperty)
        {
            var size = change.GetNewValue<double>();
            if (PlaceholderPanel.Children.Count > 0 && PlaceholderPanel.Children[0] is PathIcon icon)
            {
                icon.Width = size;
                icon.Height = size;
            }
        }
    }

    private void UpdateArtwork(string? url)
    {
        _activeUrl = url;

        if (string.IsNullOrWhiteSpace(url))
        {
            ArtworkImage.Source = null;
            ArtworkImage.IsVisible = false;
            PlaceholderPanel.IsVisible = true;
            return;
        }

        // Fast memory hit check
        var cached = ArtworkLoader.Instance.GetCached(url);
        if (cached is not null)
        {
            ArtworkImage.Source = cached;
            ArtworkImage.IsVisible = true;
            PlaceholderPanel.IsVisible = false;
            return;
        }

        // Asynchronous load with identity guard
        ArtworkImage.Source = null;
        ArtworkImage.IsVisible = false;
        PlaceholderPanel.IsVisible = true;

        var expectedUrl = url;
        _ = Task.Run(async () =>
        {
            var bitmap = await ArtworkLoader.Instance.LoadAsync(expectedUrl).ConfigureAwait(false);
            if (bitmap is not null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    // Identity guard: verify we are still displaying the requested URL!
                    if (_activeUrl == expectedUrl)
                    {
                        ArtworkImage.Source = bitmap;
                        ArtworkImage.IsVisible = true;
                        PlaceholderPanel.IsVisible = false;
                    }
                });
            }
        });
    }
}
