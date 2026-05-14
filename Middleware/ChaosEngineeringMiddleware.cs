using Microsoft.Extensions.Options;
using O11yPartyBuzzer.Services;

namespace O11yPartyBuzzer.Middleware;

public sealed class ChaosEngineeringMiddleware(
    RequestDelegate next,
    IWebHostEnvironment environment,
    IOptions<ChaosEngineeringOptions> chaosOptions,
    ILogger<ChaosEngineeringMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly IWebHostEnvironment _environment = environment;
    private readonly ChaosEngineeringOptions _chaosOptions = chaosOptions.Value;
    private readonly ILogger<ChaosEngineeringMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Query.TryGetValue("chaos", out var chaosValues))
        {
            await _next(context);
            return;
        }

        var chaosMode = chaosValues.ToString().Trim();
        if (string.IsNullOrWhiteSpace(chaosMode))
        {
            await _next(context);
            return;
        }

        AddChaosAttributes(chaosMode);

        if (_environment.IsProduction())
        {
            TryAddAttribute("chaos.mode.blocked", true);
            _logger.LogWarning(
                "Chaos mode blocked in production. RequestId: {RequestId}, ChaosMode: {ChaosMode}",
                context.TraceIdentifier,
                chaosMode);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("Chaos engineering is not permitted in production environments");
            return;
        }

        if (!_chaosOptions.Enabled)
        {
            TryAddAttribute("chaos.mode.enabled", false);
            _logger.LogInformation(
                "Chaos mode requested while disabled via configuration. RequestId: {RequestId}, ChaosMode: {ChaosMode}",
                context.TraceIdentifier,
                chaosMode);
        }
        else if (!_chaosOptions.IsEnvironmentAllowed(_environment.EnvironmentName))
        {
            TryAddAttribute("chaos.mode.enabled", false);
            _logger.LogWarning(
                "Chaos mode requested in disallowed environment {Environment}. RequestId: {RequestId}, ChaosMode: {ChaosMode}",
                _environment.EnvironmentName,
                context.TraceIdentifier,
                chaosMode);
        }

        await _next(context);
    }

    private void AddChaosAttributes(string chaosMode)
    {
        TryAddAttribute("chaos.mode.requested", true);
        TryAddAttribute("chaos.type", chaosMode);
        TryAddAttribute("chaos.environment", _environment.EnvironmentName);
    }

    private static void TryAddAttribute(string key, bool value)
    {
        NewRelic.Api.Agent.NewRelic.GetAgent().CurrentTransaction.AddCustomAttribute(key, value);
    }

    private static void TryAddAttribute(string key, string value)
    {
        NewRelic.Api.Agent.NewRelic.GetAgent().CurrentTransaction.AddCustomAttribute(key, value);
    }
}
