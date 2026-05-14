using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using O11yPartyBuzzer.Components;
using O11yPartyBuzzer.Services;

var builder = WebApplication.CreateBuilder(args);
var blockedChaosQueryParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "chaos",
    "latencyMs"
};

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.Configure<NewRelicOptions>(builder.Configuration.GetSection(NewRelicOptions.SectionName));
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

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    //app.UseHsts();
}

if (app.Environment.IsProduction())
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Query.Keys.Any(blockedChaosQueryParameters.Contains))
        {
            app.Logger.LogWarning("Blocking chaos query parameters on a production request.");

            var sanitizedQuery = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
            foreach (var queryParameter in context.Request.Query)
            {
                if (blockedChaosQueryParameters.Contains(queryParameter.Key))
                {
                    continue;
                }

                sanitizedQuery[queryParameter.Key] = queryParameter.Value;
            }

            var sanitizedQueryCollection = new QueryCollection(sanitizedQuery);
            // Keep both the parsed query feature and raw query string in sync so downstream
            // components see the same sanitized values regardless of which request API they use.
            context.Features.Set<IQueryFeature>(new QueryFeature(sanitizedQueryCollection));
            context.Request.QueryString = QueryString.Create(sanitizedQuery);
        }

        await next();
    });
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
//app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .DisableAntiforgery();

app.Run();
