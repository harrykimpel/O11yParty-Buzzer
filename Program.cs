using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.Options;
using O11yPartyBuzzer.Components;
using O11yPartyBuzzer.Services;

var builder = WebApplication.CreateBuilder(args);
var resilienceOptions = builder.Configuration.GetSection(ResilienceOptions.SectionName).Get<ResilienceOptions>() ?? new ResilienceOptions();
var blazorDisconnectedCircuitMaxRetained = Math.Max(1, resilienceOptions.BlazorDisconnectedCircuitMaxRetained);
var blazorDisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(Math.Max(1, resilienceOptions.BlazorDisconnectedCircuitRetentionMinutes));
var blazorJsInteropCallTimeout = TimeSpan.FromSeconds(Math.Max(1, resilienceOptions.BlazorJsInteropCallTimeoutSeconds));
var blazorHubClientTimeout = TimeSpan.FromSeconds(Math.Max(1, resilienceOptions.BlazorHubClientTimeoutSeconds));
var blazorHubHandshakeTimeout = TimeSpan.FromSeconds(Math.Max(1, resilienceOptions.BlazorHubHandshakeTimeoutSeconds));
var blazorHubKeepAliveInterval = TimeSpan.FromSeconds(Math.Max(1, resilienceOptions.BlazorHubKeepAliveIntervalSeconds));
var httpRequestTimeout = TimeSpan.FromSeconds(Math.Max(1, resilienceOptions.RequestTimeoutSeconds));

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DisconnectedCircuitMaxRetained = blazorDisconnectedCircuitMaxRetained;
        options.DisconnectedCircuitRetentionPeriod = blazorDisconnectedCircuitRetentionPeriod;
        options.JSInteropDefaultCallTimeout = blazorJsInteropCallTimeout;
    })
    .AddHubOptions(options =>
    {
        options.ClientTimeoutInterval = blazorHubClientTimeout;
        options.HandshakeTimeout = blazorHubHandshakeTimeout;
        options.KeepAliveInterval = blazorHubKeepAliveInterval;
    });
builder.Services.Configure<NewRelicOptions>(builder.Configuration.GetSection(NewRelicOptions.SectionName));
builder.Services.AddScoped<CircuitHandler, BlazorCircuitLoggingHandler>();
builder.Services.AddHttpClient<INewRelicEventPublisher, NewRelicEventPublisher>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<NewRelicOptions>>().Value;
    var timeoutSeconds = Math.Max(1, options.EventPublishTimeoutSeconds);
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
});
builder.Services.AddRequestTimeouts(options =>
{
    options.DefaultPolicy = new RequestTimeoutPolicy
    {
        Timeout = httpRequestTimeout,
        TimeoutStatusCode = StatusCodes.Status504GatewayTimeout
    };
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
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/_blazor"),
    appBuilder => appBuilder.UseRequestTimeouts());

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .DisableAntiforgery();

app.Run();
