namespace CoolSleep.Api.Features.NightPlan;

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CoolSleep.Api.Core;

public class ThermalClient(HttpClient http)
{
    public virtual async Task<ThermalResult> ComputeAsync(
        List<double>      temps,
        List<double>      daytimeTemps,
        List<double>      humidity,
        HousingType       housing,
        bool              voletsFermes    = true,
        double            indoorTempStart = 24.0,
        CancellationToken ct              = default)
    {
        var payload = new
        {
            hourly_temps       = temps,
            daytime_temps      = daytimeTemps,
            hourly_humidity    = humidity,
            housing            = housing.ToSnakeCase(),
            volets_fermes      = voletsFermes,
            indoor_temp_start  = indoorTempStart
        };

        var response = await http.PostAsJsonAsync("/thermal/compute", payload, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ThermalResult>(ct)
               ?? throw new InvalidOperationException("Thermal service returned null");
    }
}

// ── Résultat brut du service Python ───────────────────────────────────────
public sealed record ThermalResult(
    List<ThermalHour> Hours,
    [property: JsonPropertyName("optimal_open_hour")]    int?   OptimalOpenHour,
    [property: JsonPropertyName("optimal_close_hour")]   int?   OptimalCloseHour,
    [property: JsonPropertyName("min_indoor_reachable")] double MinIndoorReachable,
    [property: JsonPropertyName("baseline_min_indoor")]  double BaselineMinIndoor,
    [property: JsonPropertyName("min_outdoor_hour")]     int    MinOutdoorHour,
    [property: JsonPropertyName("morning_close_hour")]   int?   MorningCloseHour);

public sealed record ThermalHour(
    int    Hour,
    [property: JsonPropertyName("outdoor_temp")]            double OutdoorTemp,
    [property: JsonPropertyName("indoor_temp_estimated")]   double IndoorTempEstimated,
    [property: JsonPropertyName("heat_index")]              double HeatIndex,
    [property: JsonPropertyName("delta")]                   double Delta,
    [property: JsonPropertyName("open_window_recommended")] bool   OpenWindowRecommended);
