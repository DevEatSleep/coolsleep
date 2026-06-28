namespace CoolSleep.Api.Features.NightPlan;

using CoolSleep.Api.Core;

public static class NightPlanEndpoint
{
    public static IEndpointRouteBuilder MapNightPlan(
        this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/nightplan", async (
            NightPlanRequestBody body,
            NightPlanHandler     handler,
            CancellationToken    ct) =>
        {
            if (!Enum.TryParse<HousingType>(body.Housing, ignoreCase: true, out var housingType))
                return Results.BadRequest($"Invalid housing type: {body.Housing}");

            if (body.HourlyTemps.Count < 34 || body.HourlyHumidity.Count < 34 || body.Sunrise.Count < 2)
                return Results.BadRequest("Incomplete forecast: expected at least 34 hourly values and 2 sunrise entries.");

            var request = new NightPlanRequest(
                body.City, housingType,
                body.HourlyTemps, body.HourlyHumidity, body.Sunrise,
                body.VoletsFermes, body.IndoorTempStart);

            try
            {
                var result = await handler.HandleAsync(request, ct);
                return Results.Ok(result);
            }
            catch (HttpRequestException)
            {
                return Results.Problem(
                    title:      "Upstream service unavailable",
                    detail:     "The thermal service is temporarily unavailable, please retry in a moment.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .WithName("GetNightPlan")
        .WithSummary("Génère le plan de nuit pour une ville et un type de logement");

        return app;
    }
}
