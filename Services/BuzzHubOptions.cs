namespace O11yPartyBuzzer.Services;

public sealed class BuzzHubOptions
{
    public const string SectionName = "BuzzHub";

    public string Url { get; set; } = "";

    public string SharedSecret { get; set; } = "";
}
