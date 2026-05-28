namespace O11yPartyBuzzer.Services;

public sealed class ChaosEngineeringOptions
{
    public const string SectionName = "ChaosEngineering";

    public bool Enabled { get; set; }

    public string[] AllowedEnvironments { get; set; } = ["Development", "Staging"];

    public bool IsEnvironmentAllowed(string? environmentName)
    {
        if (string.IsNullOrWhiteSpace(environmentName))
        {
            return false;
        }

        return AllowedEnvironments.Any(candidate =>
            string.Equals(candidate, environmentName, StringComparison.OrdinalIgnoreCase));
    }
}
