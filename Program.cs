using Microsoft.AspNetCore.HttpOverrides;
using O11yPartyBuzzer.Components;
using O11yPartyBuzzer.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        // Retain disconnected circuits for reconnection attempts for up to 3 minutes.
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
        // Limit retained disconnected circuits to prevent unbounded memory growth.
        options.DisconnectedCircuitMaxRetained = 100;
        // Allow up to 30 s for JS interop calls before timing out.
        options.JSInteropDefaultCallTimeout = TimeSpan.FromSeconds(30);
    });

// Configure SignalR hub options so that stalled Blazor circuits are detected
// and cleaned up promptly rather than being left open for 90+ seconds.
builder.Services.AddSignalR(options =>
{
    // Drop a client that sends no messages for 60 s (default: 30 s).
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
    // Send a keep-alive ping every 15 s so the transport layer notices disconnects.
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    // Fail the handshake if it isn't completed within 15 s.
    options.HandshakeTimeout = TimeSpan.FromSeconds(15);
});

builder.Services.Configure<NewRelicOptions>(builder.Configuration.GetSection(NewRelicOptions.SectionName));

// Cap outbound HTTP calls to the New Relic Insights API at 10 seconds.
// Without an explicit timeout the default is 100 s, which is long enough to
// stall a Blazor SignalR circuit and trigger the 90-second response-time alert.
builder.Services.AddHttpClient<INewRelicEventPublisher, NewRelicEventPublisher>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Trust forwarded headers from App Runner's reverse proxy
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// Must be first so all subsequent middleware sees the correct scheme/IP
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    //app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
//app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .DisableAntiforgery();

app.Run();
