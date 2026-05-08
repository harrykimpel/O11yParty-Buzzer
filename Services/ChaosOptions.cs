namespace O11yPartyBuzzer.Services;

/// <summary>
/// Configuration options for the chaos/synthetic failure injection feature.
/// </summary>
public sealed class ChaosOptions
{
    public const string SectionName = "Chaos";

    /// <summary>
    /// Enables chaos/synthetic failure injection via the <c>?chaos=</c> query parameter.
    /// Must be explicitly set to <c>true</c> to activate. Defaults to <c>false</c>.
    /// Chaos mode is always disabled in the Production environment regardless of this setting.
    /// </summary>
    public bool Enabled { get; init; } = false;
}
