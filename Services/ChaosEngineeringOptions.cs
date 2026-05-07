namespace O11yPartyBuzzer.Services;

public sealed class ChaosEngineeringOptions
{
    public const string SectionName = "ChaosEngineering";

    public bool Enabled { get; set; }

    public bool AllowInProduction { get; set; }
}
