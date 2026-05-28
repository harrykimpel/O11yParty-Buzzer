using Microsoft.AspNetCore.HttpOverrides;
using O11yPartyBuzzer.Components;
using O11yPartyBuzzer.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.Configure<NewRelicOptions>(builder.Configuration.GetSection(NewRelicOptions.SectionName));
builder.Services.Configure<ChaosOptions>(builder.Configuration.GetSection(ChaosOptions.SectionName));
builder.Services.AddHttpClient<INewRelicEventPublisher, NewRelicEventPublisher>();

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

// Log chaos mode status at startup so it is always visible in the application logs
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
var chaosEnabled = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ChaosOptions>>().Value.Enabled;
if (chaosEnabled)
{
    startupLogger.LogWarning("CHAOS MODE IS ENABLED — synthetic failure injection via ?chaos= is active. " +
        "This should only be used in non-production environments.");
}
else
{
    startupLogger.LogInformation("Chaos mode is DISABLED. The ?chaos= query parameter will be ignored.");
}

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
