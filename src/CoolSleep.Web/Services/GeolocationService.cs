namespace CoolSleep.Web.Services;

using System.Globalization;
using System.Net.Http.Json;
using CoolSleep.Web.Models;
using Microsoft.JSInterop;

public sealed class GeolocationService(IJSRuntime js, HttpClient http)
{
    public async Task<GpsResult> GetCurrentLocationAsync()
    {
        var coords = await js.InvokeAsync<GpsCoords>("getGeolocation");
        string label;
        try
        {
            var url = FormattableString.Invariant(
                $"https://nominatim.openstreetmap.org/reverse?format=json&lat={coords.Latitude:F6}&lon={coords.Longitude:F6}&accept-language=fr");
            var geo = await http.GetFromJsonAsync<NominatimResponse>(url);
            label = geo?.Address?.City
                ?? geo?.Address?.Town
                ?? geo?.Address?.Village
                ?? geo?.Address?.County
                ?? $"Ma position ({coords.Latitude:F2}°, {coords.Longitude:F2}°)";
        }
        catch
        {
            label = $"Ma position ({coords.Latitude:F2}°, {coords.Longitude:F2}°)";
        }
        return new GpsResult(coords.Latitude, coords.Longitude, label);
    }

    private sealed record GpsCoords(double Latitude, double Longitude);
    private sealed record NominatimResponse(NominatimAddress? Address);
    private sealed record NominatimAddress(string? City, string? Town, string? Village, string? County);
}
