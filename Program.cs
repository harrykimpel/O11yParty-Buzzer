using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using O11yPartyBuzzer.Components;
using O11yPartyBuzzer.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Static server rendering only — NO interactive server components. The buzzer used
// to run as Blazor Server, holding a per-user SignalR circuit at /_blazor; under
// crowd load on AWS App Runner that saturated the instance (429) and dropped
// circuits (404). The buzz is now a stateless POST to the minimal-API endpoints
// below, so there is no /_blazor circuit at all.
builder.Services.AddRazorComponents();
builder.Services.Configure<NewRelicOptions>(builder.Configuration.GetSection(NewRelicOptions.SectionName));
builder.Services.AddHttpClient<INewRelicEventPublisher, NewRelicEventPublisher>();

// Trust forwarded headers from App Runner's reverse proxy
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Rate limiting — per-IP, using the real client IP resolved by UseForwardedHeaders() above.
// Policy names are referenced by .RequireRateLimiting() on each endpoint below.
var rateLimitCfg = builder.Configuration
    .GetSection(RateLimitingOptions.SectionName)
    .Get<RateLimitingOptions>() ?? new RateLimitingOptions();

builder.Services.AddRateLimiter(options =>
{
    // Return a JSON ApiError body (matching all other error responses) and a Retry-After header.
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Max(1, (int)retryAfter.TotalSeconds)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        await context.HttpContext.Response.WriteAsync(
            JsonSerializer.Serialize(new ApiError("Too many requests. Please wait a moment before trying again.")),
            cancellationToken);
    };

    // Sliding-window limiter for lead-capture: prevents boundary bursts at window edges.
    options.AddPolicy("lead-capture", context =>
    {
        var ip = NormalizeIp(context.Connection.RemoteIpAddress);
        return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = rateLimitCfg.LeadCapture.PermitLimit,
            Window = TimeSpan.FromMinutes(rateLimitCfg.LeadCapture.WindowMinutes),
            SegmentsPerWindow = rateLimitCfg.LeadCapture.SegmentsPerWindow,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
        });
    });

    // Sliding-window limiter for buzz: allows short bursts while blocking sustained spam.
    options.AddPolicy("buzz", context =>
    {
        var ip = NormalizeIp(context.Connection.RemoteIpAddress);
        return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = rateLimitCfg.Buzz.PermitLimit,
            Window = TimeSpan.FromSeconds(rateLimitCfg.Buzz.WindowSeconds),
            SegmentsPerWindow = rateLimitCfg.Buzz.SegmentsPerWindow,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
        });
    });
});

var app = builder.Build();

// Must be first so all subsequent middleware sees the correct scheme/IP
app.UseForwardedHeaders();

// Rate limiter must come after UseForwardedHeaders so the real client IP is visible.
app.UseRateLimiter();

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
app.MapRazorComponents<App>();

// --- Stateless buzz/lead API ------------------------------------------------
// Called by wwwroot/buzzer.js via fetch(). DisableAntiforgery: public, no-auth
// kiosk-style endpoints posting JSON (no cookies/session to protect).
var businessEmailRegex = new Regex(
    @"^[^\s@]+@[^\s@]+\.[^\s@]{2,}$",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

app.MapPost("/api/lead-capture", async Task<IResult> (
    LeadCaptureRequest req,
    INewRelicEventPublisher publisher) =>
{
    var firstName = req.FirstName?.Trim() ?? string.Empty;
    var lastName = req.LastName?.Trim() ?? string.Empty;
    var businessEmail = req.BusinessEmailAddress?.Trim() ?? string.Empty;
    var companyName = req.CompanyName?.Trim() ?? string.Empty;
    var jobTitle = req.JobTitle?.Trim() ?? string.Empty;
    var country = req.Country?.Trim() ?? string.Empty;

    if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName)
        || string.IsNullOrWhiteSpace(businessEmail) || string.IsNullOrWhiteSpace(companyName)
        || string.IsNullOrWhiteSpace(jobTitle) || string.IsNullOrWhiteSpace(country))
    {
        return Results.BadRequest(new ApiError("All fields are required."));
    }

    if (!businessEmailRegex.IsMatch(businessEmail))
    {
        return Results.BadRequest(new ApiError("Enter a valid business email address."));
    }

    NewRelic.Api.Agent.ITransaction transaction = NewRelic.Api.Agent.NewRelic.GetAgent().CurrentTransaction;
    transaction.AddCustomAttribute("FirstName", firstName)
        .AddCustomAttribute("LastName", lastName)
        .AddCustomAttribute("CompanyName", companyName)
        .AddCustomAttribute("JobTitle", jobTitle)
        .AddCustomAttribute("Country", country)
        .AddCustomAttribute("BusinessEmailAddress", businessEmail);

    try
    {
        await publisher.PublishLeadCaptureAsync(firstName, lastName, businessEmail, companyName, jobTitle, country);
        return Results.Ok();
    }
    catch (Exception ex)
    {
        return Results.Json(new ApiError($"Could not submit details: {ex.Message}"), statusCode: StatusCodes.Status502BadGateway);
    }
}).DisableAntiforgery().RequireRateLimiting("lead-capture");

app.MapPost("/api/buzz", async Task<IResult> (
    BuzzRequest req,
    [FromQuery] string? chaos,
    [FromQuery] int? latencyMs,
    INewRelicEventPublisher publisher,
    ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("BuzzApi");
    var teamName = req.TeamName?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(teamName))
    {
        return Results.BadRequest(new ApiError("Enter a team name before buzzing."));
    }

    NewRelic.Api.Agent.ITransaction transaction = NewRelic.Api.Agent.NewRelic.GetAgent().CurrentTransaction;

    try
    {
        await ApplySyntheticFailureAsync(chaos, latencyMs, transaction, logger);
        transaction.AddCustomAttribute("TeamName", teamName);
        await publisher.PublishBuzzAsync(teamName);
        return Results.Ok(new BuzzResponse($"Buzz received for {teamName}."));
    }
    catch (Exception ex)
    {
        return Results.Json(new ApiError($"Could not send buzz event: {ex.Message}"), statusCode: StatusCodes.Status500InternalServerError);
    }
}).DisableAntiforgery().RequireRateLimiting("buzz");

app.Run();

// --- Helpers ----------------------------------------------------------------

// Maps IPv4-mapped IPv6 addresses (::ffff:a.b.c.d) to their plain IPv4 form so
// both representations resolve to the same rate-limit bucket.
static string NormalizeIp(IPAddress? address)
{
    if (address is null) return "unknown";
    return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
}

// --- Synthetic failure modes (observability demo) ---------------------------
// New Relic failure-injection demo keeps working: ?chaos=latency|exception|random|timeout
static async Task ApplySyntheticFailureAsync(
    string? chaos,
    int? latencyMs,
    NewRelic.Api.Agent.ITransaction transaction,
    ILogger logger)
{
    var mode = (chaos ?? string.Empty).Trim().ToLowerInvariant();
    if (string.IsNullOrEmpty(mode))
    {
        return;
    }

    transaction.AddCustomAttribute("syntheticFailureMode", mode);
    logger.LogWarning("Synthetic failure mode active: {FailureMode}", mode);

    switch (mode)
    {
        case "latency":
            {
                var delay = latencyMs is > 0 ? latencyMs.Value : 3000;
                transaction.AddCustomAttribute("syntheticLatencyMs", delay);
                logger.LogWarning("Synthetic latency: {DelayMs} ms", delay);
                await Task.Delay(delay);
                break;
            }

        case "exception":
            logger.LogError("Synthetic exception triggered");
            throw new InvalidOperationException("Synthetic failure: unhandled exception injected via ?chaos=exception");

        case "random":
            {
                var roll = Random.Shared.NextDouble();
                transaction.AddCustomAttribute("syntheticChaosRoll", roll);
                if (roll < 0.5)
                {
                    logger.LogError("Synthetic random failure (roll={Roll:F2})", roll);
                    throw new InvalidOperationException($"Synthetic failure: random mode rolled {roll:F2} (< 0.5) — buzz dropped");
                }
                logger.LogInformation("Synthetic random survived (roll={Roll:F2})", roll);
                break;
            }

        case "timeout":
            {
                const int timeoutMs = 35_000;
                transaction.AddCustomAttribute("syntheticTimeoutMs", timeoutMs);
                logger.LogWarning("Synthetic timeout: waiting {TimeoutMs} ms", timeoutMs);
                using var cts = new CancellationTokenSource(timeoutMs);
                try
                {
                    await Task.Delay(Timeout.Infinite, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException("Synthetic failure: request timed out after 35 s (injected via ?chaos=timeout)");
                }
                break;
            }

        default:
            logger.LogWarning("Unknown synthetic failure mode '{Mode}' — ignored", mode);
            break;
    }
}

internal sealed record BuzzRequest(string? TeamName);
internal sealed record BuzzResponse(string Message);
internal sealed record LeadCaptureRequest(
    string? FirstName,
    string? LastName,
    string? BusinessEmailAddress,
    string? CompanyName,
    string? JobTitle,
    string? Country);
internal sealed record ApiError(string Error);
