using Microsoft.Extensions.Options;
using O11yPartyBuzzer.Services;

namespace O11yPartyBuzzer.Middleware;

public sealed class ChaosEngineeringMiddleware(
    RequestDelegate next,
    ChaosEngineeringPolicy chaosPolicy,
    ILogger<ChaosEngineeringMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ChaosEngineeringPolicy _chaosPolicy = chaosPolicy;
    private readonly ILogger<ChaosEngineeringMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Query.TryGetValue("chaos", out var chaosValues))
        {
            await _next(context);
            return;
        }

        var evaluation = _chaosPolicy.Evaluate(chaosValues.ToString());
        if (!evaluation.IsRequested)
        {
            await _next(context);
            return;
        }

        AddChaosAttributes(evaluation);

        if (evaluation.IsBlockedInProduction)
        {
            TryAddAttribute("chaos.mode.blocked", true);
            _logger.LogWarning(
                "Chaos mode blocked in production. RequestId: {RequestId}",
                context.TraceIdentifier);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("Chaos engineering is not permitted in production environments");
            return;
        }

        if (evaluation.IsDisabledByConfiguration)
        {
            TryAddAttribute("chaos.mode.enabled", false);
            _logger.LogInformation(
                "Chaos mode requested while disabled via configuration. RequestId: {RequestId}",
                context.TraceIdentifier);
        }
        else if (evaluation.IsDisallowedEnvironment)
        {
            TryAddAttribute("chaos.mode.enabled", false);
            _logger.LogWarning(
                "Chaos mode requested in a disallowed environment. RequestId: {RequestId}.",
                context.TraceIdentifier);
        }

        await _next(context);
    }

    private void AddChaosAttributes(ChaosRequestEvaluation evaluation)
    {
        TryAddAttribute(transaction =>
        {
            transaction.AddCustomAttribute("chaos.mode.requested", true);
            transaction.AddCustomAttribute("chaos.type", evaluation.Mode);
            transaction.AddCustomAttribute("chaos.environment", evaluation.EnvironmentName);
        });
    }

    private void TryAddAttribute(string key, bool value)
    {
        TryAddAttribute(transaction => transaction.AddCustomAttribute(key, value));
    }

    private void TryAddAttribute(string key, string value)
    {
        TryAddAttribute(transaction => transaction.AddCustomAttribute(key, value));
    }

    private void TryAddAttribute(Action<NewRelic.Api.Agent.ITransaction> applyAttribute)
    {
        var transaction = NewRelic.Api.Agent.NewRelic.GetAgent().CurrentTransaction;
        if (transaction is null)
        {
            _logger.LogDebug("Skipped New Relic chaos attribute because no active transaction was available.");
            return;
        }

        applyAttribute(transaction);
    }
}
