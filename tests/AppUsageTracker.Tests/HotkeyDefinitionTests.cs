using AppUsageTracker.Models;

namespace AppUsageTracker.Tests;

public sealed class HotkeyDefinitionTests
{
    [Theory]
    [InlineData("Ctrl+Alt+T")]
    [InlineData("Ctrl+Shift+F5")]
    [InlineData("Alt+1")]
    [InlineData("Ctrl+Alt+Shift+Win+Z")]
    public void RoundTripsThroughString(string text)
    {
        var definition = HotkeyDefinition.Parse(text);

        Assert.True(definition.IsValid);
        Assert.Equal(text, definition.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("T")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+?")]
    public void InvalidInputsProduceNone(string text)
    {
        Assert.False(HotkeyDefinition.Parse(text).IsValid);
    }

    [Fact]
    public void FromKeyBuildsExpectedDefinition()
    {
        var definition = HotkeyDefinition.FromKey('T', HotkeyModifiers.Control | HotkeyModifiers.Alt);

        Assert.True(definition.IsValid);
        Assert.Equal("Ctrl+Alt+T", definition.ToString());
    }
}
