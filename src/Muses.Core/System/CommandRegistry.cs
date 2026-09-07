namespace Muses.Core.System;

public sealed class CommandRegistry
{
    public const string TogglePlayback = "player.togglePlayback";
    public const string Next = "player.next";
    public const string Previous = "player.previous";
    public const string LikeCurrent = "library.likeCurrent";
    public const string ToggleQueue = "ui.toggleQueue";
    public const string ToggleNowPlaying = "ui.toggleNowPlaying";
    public const string FocusSearch = "ui.focusSearch";
    public const string PasteYouTube = "library.pasteYouTube";

    private readonly Dictionary<string, Action> _handlers = new();
    private readonly Dictionary<string, Func<bool>> _enabled = new();

    public void Register(string id, Action handler, Func<bool>? enabled = null)
    {
        _handlers[id] = handler;
        _enabled[id] = enabled ?? (() => true);
    }

    public void Execute(string id)
    {
        if (_handlers.TryGetValue(id, out var handler)) handler();
    }

    public bool IsEnabled(string id) => _enabled.TryGetValue(id, out var check) && check();
    public bool Has(string id) => _handlers.ContainsKey(id);
}
