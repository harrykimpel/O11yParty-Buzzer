using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using O11yPartyBuzzer.Components;
using O11yPartyBuzzer.Services;

var builder = WebApplication.CreateBuilder(args);
var blazorServerOptions = builder.Configuration.GetSection(BlazorServerOptions.SectionName).Get<BlazorServerOptions>() ?? new();

builder.Services.AddOptions<BlazorServerOptions>()
    .BindConfiguration(BlazorServerOptions.SectionName)
    .Validate(
        options => options.ClientTimeoutInterval > options.KeepAliveInterval &&
                   options.KeepAliveInterval > TimeSpan.Zero &&
                   options.HandshakeTimeout > TimeSpan.Zero &&
                   options.DisconnectedCircuitMaxRetained > 0 &&
                   options.DisconnectedCircuitRetentionPeriod > TimeSpan.Zero &&
                   options.JSInteropDefaultCallTimeout > TimeSpan.Zero &&
                   options.UnhealthyDisconnectedCircuitThreshold > 0,
        "BlazorServer settings must be positive and ClientTimeoutInterval must be greater than KeepAliveInterval.")
    .ValidateOnStart();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DisconnectedCircuitMaxRetained = blazorServerOptions.DisconnectedCircuitMaxRetained;
        options.DisconnectedCircuitRetentionPeriod = blazorServerOptions.DisconnectedCircuitRetentionPeriod;
        options.JSInteropDefaultCallTimeout = blazorServerOptions.JSInteropDefaultCallTimeout;
    });
builder.Services.AddServerSideBlazor()
    .AddHubOptions(options =>
    {
        options.ClientTimeoutInterval = blazorServerOptions.ClientTimeoutInterval;
        options.KeepAliveInterval = blazorServerOptions.KeepAliveInterval;
        options.HandshakeTimeout = blazorServerOptions.HandshakeTimeout;
    });
builder.Services.Configure<NewRelicOptions>(builder.Configuration.GetSection(NewRelicOptions.SectionName));
builder.Services.AddHttpClient<INewRelicEventPublisher, NewRelicEventPublisher>();
builder.Services.AddSingleton<BlazorCircuitMonitor>();
builder.Services.AddSingleton<CircuitHandler, CircuitHandlerService>();
builder.Services.AddHealthChecks()
    .AddCheck<BlazorCircuitHealthCheck>("blazor_circuits");

// Trust forwarded headers from App Runner's reverse proxy
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
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
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready");
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .DisableAntiforgery();

app.Run();
