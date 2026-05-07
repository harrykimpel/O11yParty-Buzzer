using Microsoft.AspNetCore.HttpOverrides;
using O11yPartyBuzzer.Components;
using O11yPartyBuzzer.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddJsonConsole();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.Configure<NewRelicOptions>(builder.Configuration.GetSection(NewRelicOptions.SectionName));
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<INewRelicEventPublisher, NewRelicEventPublisher>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<NewRelicOptions>>().Value;
    var timeoutSeconds = options.RequestTimeoutSeconds > 0 ? options.RequestTimeoutSeconds : 3;
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
})
.ConfigurePrimaryHttpMessageHandler(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<NewRelicOptions>>().Value;
    var maxConnectionsPerServer = options.MaxConnectionsPerServer > 0 ? options.MaxConnectionsPerServer : 32;
    var pooledConnectionLifetimeSeconds = options.PooledConnectionLifetimeSeconds > 0 ? options.PooledConnectionLifetimeSeconds : 300;

    return new SocketsHttpHandler
    {
        MaxConnectionsPerServer = maxConnectionsPerServer,
        PooledConnectionLifetime = TimeSpan.FromSeconds(pooledConnectionLifetimeSeconds)
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

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .DisableAntiforgery();

app.Run();
