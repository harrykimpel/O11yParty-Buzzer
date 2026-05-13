namespace O11yPartyBuzzer.Services;

public sealed class ChaosOptions
{
    public const string SectionName = "Chaos";

    /// <summary>
    /// When false (the default), the ?chaos= query parameter is ignored and no synthetic
    /// failures are injected.  Set to true only in non-production environments.
    /// </summary>
    public bool Enabled { get; set; } = false;
}
