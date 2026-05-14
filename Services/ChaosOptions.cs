namespace O11yPartyBuzzer.Services;

public sealed class ChaosOptions
{
    public const string SectionName = "Chaos";

    public bool EnableChaosMode { get; set; } = false;

    public List<string> AllowedEnvironments { get; set; } = ["Development", "Staging"];
}
