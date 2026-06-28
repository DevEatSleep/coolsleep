namespace CoolSleep.Api.Features.NightPlan;

using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;

public class OpenMeteoClient(HttpClient http, IMemoryCache? cache = null)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
        { PropertyNameCaseInsensitive = true };

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Récupère la météo horaire 10h→17h (journée) + 18h→09h (nuit) + lever du soleil J+1.
    /// </summary>
    public virtual async Task<(List<double> Temps, List<double> DaytimeTemps, List<double> Humidity, TimeOnly Sunrise)> GetNightForecastAsync(
        double lat, double lon, CancellationToken ct = default)
    {
        var key = $"forecast:{lat.ToString("F2", CultureInfo.InvariantCulture)}:{lon.ToString("F2", CultureInfo.InvariantCulture)}";
        if (cache is not null
            && cache.TryGetValue(key, out (List<double>, List<double>, List<double>, TimeOnly) hit))
            return hit;

        var url = $"https://api.open-meteo.com/v1/forecast"
                + $"?latitude={lat.ToString("F4", CultureInfo.InvariantCulture)}"
                + $"&longitude={lon.ToString("F4", CultureInfo.InvariantCulture)}"
                + $"&hourly=temperature_2m,relativehumidity_2m"
                + $"&daily=sunrise"
                + $"&forecast_days=2&timezone=Europe%2FParis";

        var result = await FetchWithRetryAsync(url, ct);
        cache?.Set(key, result, TimeSpan.FromHours(1));
        return result;
    }

    private static async Task<(List<double>, List<double>, List<double>, TimeOnly)> ParseAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
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

    private async Task<(List<double>, List<double>, List<double>, TimeOnly)> FetchWithRetryAsync(
        string url, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var response = await http.GetAsync(url, ct);
            if (response.IsSuccessStatusCode)
                return await ParseAsync(response, ct);

            var transient = response.StatusCode == HttpStatusCode.TooManyRequests
                            || (int)response.StatusCode >= 500;
            if (transient && attempt < maxAttempts)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt);
                await Task.Delay(wait < MaxBackoff ? wait : MaxBackoff, ct);
                continue;
            }

            response.EnsureSuccessStatusCode(); // throws HttpRequestException for the endpoint to catch
        }
        throw new InvalidOperationException("Open-Meteo unreachable after retries");
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
