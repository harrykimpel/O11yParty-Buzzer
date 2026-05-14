using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Extensions;
using O11yPartyBuzzer.Components;
using O11yPartyBuzzer.Services;

var builder = WebApplication.CreateBuilder(args);

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
        if (context.Request.Query.ContainsKey("chaos") || context.Request.Query.ContainsKey("latencyMs"))
        {
            var attemptedMode = context.Request.Query["chaos"].ToString();
            app.Logger.LogWarning(
                "Blocking chaos query parameters on production request to {Path}. chaos={ChaosMode}",
                context.Request.Path,
                attemptedMode);

            var sanitizedQuery = new QueryBuilder();
            foreach (var queryParameter in context.Request.Query)
            {
                if (string.Equals(queryParameter.Key, "chaos", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(queryParameter.Key, "latencyMs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var value in queryParameter.Value)
                {
                    sanitizedQuery.Add(queryParameter.Key, value ?? string.Empty);
                }
            }

            context.Request.QueryString = sanitizedQuery.ToQueryString();
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
