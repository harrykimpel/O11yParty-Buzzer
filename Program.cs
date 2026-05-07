using Microsoft.AspNetCore.HttpOverrides;
using O11yPartyBuzzer.Components;
using O11yPartyBuzzer.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.Configure<NewRelicOptions>(builder.Configuration.GetSection(NewRelicOptions.SectionName));
builder.Services.Configure<ChaosEngineeringOptions>(builder.Configuration.GetSection(ChaosEngineeringOptions.SectionName));
builder.Services.AddSingleton<ChaosModePolicy>();
builder.Services.AddHttpClient<INewRelicEventPublisher, NewRelicEventPublisher>();

// Trust forwarded headers from App Runner's reverse proxy
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
var chaosModePolicy = app.Services.GetRequiredService<ChaosModePolicy>();
chaosModePolicy.ValidateStartupConfiguration();

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

app.Use(async (context, next) =>
{
    if (app.Environment.IsProduction() && context.Request.Query.ContainsKey("chaos"))
    {
        startupLogger.LogWarning(
            "Chaos query parameter detected for {Path} in production and will be ignored",
            context.Request.Path);
    }

    await next();
});

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .DisableAntiforgery();

app.Run();
