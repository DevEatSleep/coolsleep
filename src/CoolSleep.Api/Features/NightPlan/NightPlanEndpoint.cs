namespace CoolSleep.Api.Features.NightPlan;

using CoolSleep.Api.Core;

public static class NightPlanEndpoint
{
    public static IEndpointRouteBuilder MapNightPlan(
        this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/nightplan", async (
            string           city,
            double           lat,
            double           lon,
            string           housing,
            NightPlanHandler handler,
            CancellationToken ct,
            bool?            volets               = null,
            double?          indoor_temp_start    = null) =>
        {
            if (!Enum.TryParse<HousingType>(housing, ignoreCase: true, out var housingType))
                return Results.BadRequest($"Invalid housing type: {housing}");

            var request = new NightPlanRequest(
                city, lat, lon, housingType,
                volets ?? true,
                indoor_temp_start ?? 24.0);

            try
            {
                var result = await handler.HandleAsync(request, ct);
                return Results.Ok(result);
            }
            catch (HttpRequestException)
            {
                return Results.Problem(
                    title:      "Upstream service unavailable",
                    detail:     "Weather data is temporarily unavailable, please retry in a moment.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .WithName("GetNightPlan")
        .WithSummary("Génère le plan de nuit pour une ville et un type de logement");

        return app;
    }
}
