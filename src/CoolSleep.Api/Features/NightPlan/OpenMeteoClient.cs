namespace CoolSleep.Api.Features.NightPlan;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

public class OpenMeteoClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
        { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Récupère la météo horaire 10h→17h (journée) + 18h→09h (nuit) + lever du soleil J+1.
    /// </summary>
    public virtual async Task<(List<double> Temps, List<double> DaytimeTemps, List<double> Humidity, TimeOnly Sunrise)> GetNightForecastAsync(
        double lat, double lon, CancellationToken ct = default)
    {
        var url = $"https://api.open-meteo.com/v1/forecast"
                + $"?latitude={lat.ToString("F4", CultureInfo.InvariantCulture)}"
                + $"&longitude={lon.ToString("F4", CultureInfo.InvariantCulture)}"
                + $"&hourly=temperature_2m,relativehumidity_2m"
                + $"&daily=sunrise"
                + $"&forecast_days=2&timezone=Europe%2FParis";

        await using var stream = await http.GetStreamAsync(url, ct);
        var data = await JsonSerializer.DeserializeAsync<OpenMeteoResponse>(stream, JsonOpts, ct)
                   ?? throw new InvalidOperationException("Open-Meteo returned null");

        // Heures 10→17 jour J (8 valeurs pour le warmup diurne)
        var daytimeTemps = data.Hourly.Temperature2m.Skip(10).Take(8).ToList();
        // Heures 18→33 (18h jour J → 09h jour J+1)
        var temps    = data.Hourly.Temperature2m.Skip(18).Take(16).ToList();
        var humidity = data.Hourly.Relativehumidity2m.Skip(18).Take(16).ToList();

        // Lever du soleil J+1 : "2024-06-25T05:48" ou "2024-06-25T05:48:00"
        var raw     = data.Daily.Sunrise[1];
        var sunrise = TimeOnly.FromDateTime(DateTime.Parse(raw, CultureInfo.InvariantCulture));

        return (temps, daytimeTemps, humidity, sunrise);
    }

    private sealed record OpenMeteoResponse(HourlyPayload Hourly, DailyPayload Daily);

    private sealed record HourlyPayload(
        [property: JsonPropertyName("temperature_2m")]
        List<double> Temperature2m,
        [property: JsonPropertyName("relativehumidity_2m")]
        List<double> Relativehumidity2m);

    private sealed record DailyPayload(
        [property: JsonPropertyName("sunrise")]
        List<string> Sunrise);
}
