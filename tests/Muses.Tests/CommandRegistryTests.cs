using Muses.Core.System;

namespace Muses.Tests;

public class CommandRegistryTests
{
    [Fact]
    public void Execute_invokes_registered_handler()
    {
        var registry = new CommandRegistry();
        var n = 0;
        registry.Register(CommandRegistry.TogglePlayback, () => n++);
        registry.Execute(CommandRegistry.TogglePlayback);
        registry.Execute("missing");
        Assert.Equal(1, n);
        Assert.True(registry.Has(CommandRegistry.TogglePlayback));
        Assert.True(registry.IsEnabled(CommandRegistry.TogglePlayback));
    }
}
