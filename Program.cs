using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
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
var chaosOptions = app.Services.GetRequiredService<IOptions<ChaosOptions>>().Value;
ChaosConfigurationValidator.ValidateOrThrow(chaosOptions, app.Environment);
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("O11yPartyBuzzer.Startup.ChaosMode");
if (chaosOptions.Enabled)
{
    startupLogger.LogWarning("Chaos mode is ENABLED for environment '{EnvironmentName}'.", app.Environment.EnvironmentName);
}
else
{
    startupLogger.LogInformation("Chaos mode is disabled for environment '{EnvironmentName}'.", app.Environment.EnvironmentName);
}

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
