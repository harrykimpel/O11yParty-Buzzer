namespace O11yPartyBuzzer.Services;

public sealed class ChaosOptions
{
    public const string SectionName = "Chaos";

    /// <summary>
    /// Controls whether the synthetic failure injection via the <c>?chaos=</c> query parameter
    /// is active. Defaults to <c>false</c> so that production deployments are protected.
    /// Enable only in non-production environments that are explicitly configured for chaos testing.
    /// </summary>
    public bool Enabled { get; set; } = false;
}
