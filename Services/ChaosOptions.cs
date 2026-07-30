namespace O11yPartyBuzzer.Services;

public sealed class ChaosOptions
{
    public const string SectionName = "Chaos";

    /// <summary>
    /// Whether synthetic chaos/failure modes are enabled.
    /// Must be <c>false</c> (the default) in production; set to <c>true</c>
    /// only in development or staging via appsettings.Development.json or
    /// environment-specific configuration.
    /// </summary>
    public bool Enabled { get; init; } = false;
}
