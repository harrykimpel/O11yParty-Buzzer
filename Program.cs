using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using O11yPartyBuzzer.Components;
using O11yPartyBuzzer.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.Configure<NewRelicOptions>(builder.Configuration.GetSection(NewRelicOptions.SectionName));

// Set a short timeout so a slow or unreachable New Relic ingest endpoint never
// blocks a request for the default 100 seconds.
builder.Services.AddHttpClient<INewRelicEventPublisher, NewRelicEventPublisher>()
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(10));

// Persist data-protection keys to a configurable directory so that keys survive
// container restarts (point DataProtection:KeysPath at an EFS/persistent volume
// in production to share keys across multiple Fargate tasks).
var configuredKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(configuredKeysPath) && !Path.IsPathRooted(configuredKeysPath))
{
    throw new InvalidOperationException(
        $"DataProtection:KeysPath must be an absolute path, but '{configuredKeysPath}' was provided.");
}
var keysPath = configuredKeysPath ?? Path.Combine(builder.Environment.ContentRootPath, "keys");
builder.Services.AddDataProtection()
    .SetApplicationName("O11yParty-Buzzer")
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

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
