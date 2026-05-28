namespace O11yPartyBuzzer.Services;

public sealed class ChaosOptions
{
    public const string SectionName = "ChaosEngineering";

    /// <summary>
    /// When false (the default), all ?chaos=* query parameters are ignored
    /// regardless of environment. Set to true only in non-production
    /// environments to allow synthetic failure injection.
    /// </summary>
    public bool Enabled { get; set; } = false;
}
