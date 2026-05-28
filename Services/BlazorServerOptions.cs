namespace O11yPartyBuzzer.Services;

public sealed class BlazorServerOptions
{
    public const string SectionName = "BlazorServer";

    public TimeSpan ClientTimeoutInterval { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(15);

    public int DisconnectedCircuitMaxRetained { get; set; } = 100;

    public TimeSpan DisconnectedCircuitRetentionPeriod { get; set; } = TimeSpan.FromMinutes(3);

    public TimeSpan JSInteropDefaultCallTimeout { get; set; } = TimeSpan.FromMinutes(1);

    public int UnhealthyDisconnectedCircuitThreshold { get; set; } = 10;
}
