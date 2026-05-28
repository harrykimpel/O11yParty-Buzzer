namespace O11yPartyBuzzer.Services;

public static class ChaosConfigurationValidator
{
    public static void ValidateOrThrow(ChaosOptions options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        if (environment.IsProduction() && options.Enabled)
        {
            throw new InvalidOperationException("Chaos mode cannot be enabled in Production. Set Chaos:Enabled (or Chaos__Enabled) to false.");
        }
    }
}
