namespace O11yPartyBuzzer.Services;

public sealed class ChaosOptions
{
    public const string SectionName = "Chaos";

    /// <summary>
    /// Controls whether chaos testing modes (?chaos=...) are permitted.
    /// Defaults to <c>false</c>; enable only in non-production environments.
    /// </summary>
    public bool Enabled { get; set; }
}
