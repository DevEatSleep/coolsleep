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
            var result  = await handler.HandleAsync(request, ct);
            return Results.Ok(result);
        })
        .WithName("GetNightPlan")
        .WithSummary("Génère le plan de nuit pour une ville et un type de logement");

        return app;
    }
}
