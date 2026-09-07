using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Muses.App.ViewModels;
using Muses.Core.Lyrics;
using Muses.App.Theme;

namespace Muses.App.Views;

public partial class LyricsDrawerView : UserControl
{
    private ShellViewModel? _vm;
    private readonly List<TextBlock> _lineBlocks = [];

    public LyricsDrawerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm?.Lyrics is not null)
        {
            _vm.Lyrics.Changed -= OnLyricsChanged;
        }

        _vm = DataContext as ShellViewModel;
        if (_vm?.Lyrics is not null)
        {
            _vm.Lyrics.Changed += OnLyricsChanged;
            UpdateLyrics();
        }
    }

    private void OnLyricsChanged()
    {
        Dispatcher.UIThread.Post(UpdateLyrics);
    }

    private void UpdateLyrics()
    {
        if (_vm?.Lyrics is null) return;
        var lyrics = _vm.Lyrics;

        OffsetLabel.Text = $"{lyrics.ManualOffsetMs / 1000.0:+0.0;-0.0;0.0}s";

        // Rebuild lines if line count or lyrics reference changed
        if (_lineBlocks.Count != lyrics.Lines.Count)
        {
            LyricsLinesContainer.Children.Clear();
            _lineBlocks.Clear();

            foreach (var line in lyrics.Lines)
            {
                var btn = new Button
                {
                    Classes = { "lyricLine" },
                    Tag = line
                };

                var tb = new TextBlock
                {
                    Text = line.Text,
                    FontSize = 14,
                    FontWeight = FontWeight.Medium,
                    Foreground = ThemeBrushes.WhiteOverlay73,
                    TextWrapping = TextWrapping.Wrap
                };
                _lineBlocks.Add(tb);
                btn.Content = tb;

                if (line.TimeMs.HasValue)
                {
                    var seekSec = line.TimeMs.Value / 1000.0;
                    btn.Click += (_, _) => _vm.SeekToSeconds(seekSec);
                }

                LyricsLinesContainer.Children.Add(btn);
            }
        }

        // Highlight active line
        var activeIdx = lyrics.CurrentLineIndex;
        for (var i = 0; i < _lineBlocks.Count; i++)
        {
            var tb = _lineBlocks[i];
            if (i == activeIdx)
            {
                tb.FontSize = 17;
                tb.FontWeight = FontWeight.Bold;
                tb.Foreground = Brushes.White;
            }
            else
            {
                tb.FontSize = 14;
                tb.FontWeight = FontWeight.Medium;
                tb.Foreground = ThemeBrushes.WhiteOverlay73;
            }
        }

        // Auto-scroll to active line
        if (activeIdx >= 0 && activeIdx < LyricsLinesContainer.Children.Count)
        {
            var activeElement = LyricsLinesContainer.Children[activeIdx];
            activeElement.BringIntoView();
        }
    }

    private void OnMinusOffset(object? sender, RoutedEventArgs e)
    {
        if (_vm?.Lyrics is not null)
        {
            _vm.Lyrics.SetManualOffset(_vm.Lyrics.ManualOffsetMs - 500);
        }
    }

    private void OnPlusOffset(object? sender, RoutedEventArgs e)
    {
        if (_vm?.Lyrics is not null)
        {
            _vm.Lyrics.SetManualOffset(_vm.Lyrics.ManualOffsetMs + 500);
        }
    }
}
