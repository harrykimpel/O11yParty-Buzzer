using Microsoft.Extensions.Options;

namespace O11yPartyBuzzer.Services;

public sealed class ChaosModeGuardMiddleware(
    RequestDelegate next,
    ILogger<ChaosModeGuardMiddleware> logger,
    IHostEnvironment environment,
    IOptions<ChaosOptions> chaosOptions)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Query.ContainsKey("chaos"))
        {
            await next(context);
            return;
        }

        if (!chaosOptions.Value.Enabled)
        {
            logger.LogWarning("Blocked chaos mode query parameter while disabled.");
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Chaos mode is disabled.");
            return;
        }

        if (environment.IsProduction())
        {
            logger.LogWarning("Blocked chaos mode query parameter in production.");
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Chaos mode is not available in production.");
            return;
        }

        await next(context);
    }
}
