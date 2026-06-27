namespace CoolSleep.Api.Features.NightPlan;

using CoolSleep.Api.Core;

public sealed class NightPlanHandler(
    OpenMeteoClient openMeteo,
    ThermalClient   thermal)
{
    public async Task<NightPlanResponse> HandleAsync(
        NightPlanRequest  request,
        CancellationToken ct = default)
    {
        // 1. Météo horaire Open-Meteo
        var (temps, daytimeTemps, humidity, sunrise) = await openMeteo.GetNightForecastAsync(
            request.Lat, request.Lon, ct);

        // 2. Calcul thermique (micro-service Python)
        var thermalResult = await thermal.ComputeAsync(
            temps, daytimeTemps, humidity, request.Housing,
            voletsFermes: request.VoletsFermes,
            indoorTempStart: request.IndoorTempStart,
            ct: ct);

        // 3. Construction du plan (Core)
        var hours = thermalResult.Hours
            .Select(h => new HourlyData(
                h.Hour, h.OutdoorTemp, h.IndoorTempEstimated,
                h.HeatIndex, h.OpenWindowRecommended))
            .ToList();

        var plan = NightPlanEngine.Build(
            request.City,
            hours,
            thermalResult.OptimalOpenHour,
            thermalResult.OptimalCloseHour,
            thermalResult.MinIndoorReachable,
            request.Housing,
            sunrise,
            thermalResult.MinOutdoorHour,
            thermalResult.MorningCloseHour,
            request.VoletsFermes);

        // 4. Projection → Response
        // Note: MinOutdoorTemp corresponds to MinOutdoorHour in thermalResult,
        // but we extract from Hours for clarity. AvgHumidity is derived from hourly data.
        var minOutdoorHourData = thermalResult.Hours.FirstOrDefault(h => h.Hour == thermalResult.MinOutdoorHour);
        double minOutdoor  = minOutdoorHourData?.OutdoorTemp ?? 0;
        int    avgHumidity = (int)Math.Round(humidity.Average());

        return new NightPlanResponse(
            City:                  plan.City,
            RiskLevel:             plan.RiskLevel.ToString(),
            RiskScore:             plan.RiskScore,
            MinIndoorTemp:         plan.MinIndoorTemp,
            BaselineMinIndoorTemp: thermalResult.BaselineMinIndoor,
            MinOutdoorTemp:        minOutdoor,
            AvgHumidity:           avgHumidity,
            OptimalOpenHour:       plan.OptimalOpenHour,
            OptimalCloseHour:      plan.OptimalCloseHour,
            Actions: plan.Actions
                .Select(a => new NightActionResponse(
                    a.Hour, a.Label, a.Detail, a.ActionType.ToString()))
                .ToList());
    }
}
