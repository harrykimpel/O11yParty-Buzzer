using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.Options;
using O11yPartyBuzzer.Components;
using O11yPartyBuzzer.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.Configure<NewRelicOptions>(builder.Configuration.GetSection(NewRelicOptions.SectionName));
builder.Services.AddOptions<HttpConnectionDispatcherOptions>()
    .Configure<IOptions<NewRelicOptions>>((options, newRelicOptions) =>
    {
        var timeoutSeconds = newRelicOptions.Value.SignalRLongPollingTimeoutSeconds > 0
            ? newRelicOptions.Value.SignalRLongPollingTimeoutSeconds
            : NewRelicOptions.DefaultSignalRLongPollingTimeoutSeconds;

        options.LongPolling.PollTimeout = TimeSpan.FromSeconds(timeoutSeconds);
    });
builder.Services.AddHttpClient<INewRelicEventPublisher, NewRelicEventPublisher>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<NewRelicOptions>>().Value;
    var timeoutSeconds = options.PublishTimeoutSeconds > 0
        ? options.PublishTimeoutSeconds
        : NewRelicOptions.DefaultPublishTimeoutSeconds;
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
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
