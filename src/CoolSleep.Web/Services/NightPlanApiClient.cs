namespace CoolSleep.Web.Services;

using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CoolSleep.Web.Models;

public sealed class NightPlanApiClient(HttpClient http)
{
    public async Task<NightPlanModel?> GetAsync(
        string city, double lat, double lon, string housing, bool voletsFermes = true,
        double indoorTempStart = 24.0, bool debug = false)
    {
        // 1. Météo récupérée directement depuis le navigateur de l'utilisateur :
        //    la requête part de SON IP (quota Open-Meteo dédié), pas de l'IP
        //    partagée de l'hébergeur — ce qui évite le rate-limit 429.
        var meteoUrl = $"https://api.open-meteo.com/v1/forecast"
                     + $"?latitude={lat.ToString("F4", CultureInfo.InvariantCulture)}"
                     + $"&longitude={lon.ToString("F4", CultureInfo.InvariantCulture)}"
                     + $"&hourly=temperature_2m,relativehumidity_2m"
                     + $"&daily=sunrise"
                     + $"&forecast_days=2&timezone=Europe%2FParis";

        var meteo = await http.GetFromJsonAsync<OpenMeteoResponse>(meteoUrl)
                    ?? throw new InvalidOperationException("Open-Meteo returned null");

        // 2. On transmet la météo brute à notre API (découpage + modèle thermique
        //    restent côté serveur).
        var body = new NightPlanRequestBody(
            City:            city,
            Housing:         housing,
            HourlyTemps:     meteo.Hourly.Temperature2m,
            HourlyHumidity:  meteo.Hourly.Relativehumidity2m,
            Sunrise:         meteo.Daily.Sunrise,
            VoletsFermes:    voletsFermes,
            IndoorTempStart: indoorTempStart,
            Debug:           debug);

        var response = await http.PostAsJsonAsync("api/nightplan", body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NightPlanModel>();
    }

    // ── Corps de requête envoyé à CoolSleep.Api ───────────────────────────
    private sealed record NightPlanRequestBody(
        string       City,
        string       Housing,
        List<double> HourlyTemps,
        List<double> HourlyHumidity,
        List<string> Sunrise,
        bool         VoletsFermes,
        double       IndoorTempStart,
        bool         Debug);

    // ── Réponse Open-Meteo ────────────────────────────────────────────────
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
