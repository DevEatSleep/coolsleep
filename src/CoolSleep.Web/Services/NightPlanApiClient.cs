namespace CoolSleep.Web.Services;

using System.Globalization;
using System.Net.Http.Json;
using CoolSleep.Web.Models;

public sealed class NightPlanApiClient(HttpClient http)
{
    public async Task<NightPlanModel?> GetAsync(
        string city, double lat, double lon, string housing, bool voletsFermes = true,
        double indoorTempStart = 24.0)
    {
        var url = $"api/nightplan"
                + $"?city={Uri.EscapeDataString(city)}"
                + $"&lat={lat.ToString("F4", CultureInfo.InvariantCulture)}"
                + $"&lon={lon.ToString("F4", CultureInfo.InvariantCulture)}"
                + $"&housing={housing}"
                + $"&volets={voletsFermes.ToString().ToLower()}";

        if (indoorTempStart != 24.0)
            url += $"&indoor_temp_start={indoorTempStart.ToString("F1", CultureInfo.InvariantCulture)}";

        return await http.GetFromJsonAsync<NightPlanModel>(url);
    }
}
