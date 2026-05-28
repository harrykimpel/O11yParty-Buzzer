namespace O11yPartyBuzzer.Services;

public sealed class ChaosOptions
{
    public const string SectionName = "Chaos";

    /// <summary>
    /// When <c>false</c> (the default), the <c>?chaos=</c> query-parameter is silently ignored
    /// so that synthetic failure injection cannot be triggered in production.
    /// Set to <c>true</c> only in non-production environments where chaos testing is intentional.
    /// </summary>
    public bool Enabled { get; set; } = false;
}
