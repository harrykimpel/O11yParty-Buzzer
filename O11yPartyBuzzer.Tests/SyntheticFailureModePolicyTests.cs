using O11yPartyBuzzer.Services;

namespace O11yPartyBuzzer.Tests;

public sealed class SyntheticFailureModePolicyTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("  ExCePtIoN  ", "exception")]
    [InlineData(" latency ", "latency")]
    public void Normalize_StandardizesMode(string? input, string expected)
    {
        var normalized = SyntheticFailureModePolicy.Normalize(input);

        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void IsEnabled_ReturnsFalseInProduction()
    {
        var enabled = SyntheticFailureModePolicy.IsEnabled("exception", isProductionEnvironment: true);

        Assert.False(enabled);
    }

    [Fact]
    public void IsEnabled_ReturnsFalseWhenModeIsEmpty()
    {
        var enabled = SyntheticFailureModePolicy.IsEnabled(string.Empty, isProductionEnvironment: false);

        Assert.False(enabled);
    }

    [Fact]
    public void IsEnabled_ReturnsTrueForNonProductionWithMode()
    {
        var enabled = SyntheticFailureModePolicy.IsEnabled("random", isProductionEnvironment: false);

        Assert.True(enabled);
    }

    [Fact]
    public void SanitizeForLog_ReplacesLineBreaks()
    {
        var sanitized = SyntheticFailureModePolicy.SanitizeForLog("except\r\nion");

        Assert.Equal(@"except\r\nion", sanitized);
    }
}
