namespace CoolSleep.Web.Services;

using System.Net.Http.Json;
using CoolSleep.Web.Models;
using Microsoft.AspNetCore.Components;

public sealed class CityDirectoryService(HttpClient http, NavigationManager nav)
{
    private CityOption[] _allCities = [];

    public IReadOnlyList<CityOption> AllCities => _allCities;

    public async Task InitializeAsync()
    {
        var url = nav.BaseUri.TrimEnd('/') + "/data/cities-fr.json";
        var data = await http.GetFromJsonAsync<CityData[]>(url);
        if (data != null)
            _allCities = data.Select(d => new CityOption(d.Name, d.Lat, d.Lon)).ToArray();
    }

    public Task<IEnumerable<CityOption>> SearchCities(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Task.FromResult(_allCities.AsEnumerable().Take(12));
        var lower = StripAccents(value.Trim().ToLowerInvariant());
        var results = _allCities
            .Where(c => StripAccents(c.Name.ToLowerInvariant()).StartsWith(lower))
            .OrderBy(c => c.Name)
            .Take(12);
        return Task.FromResult(results);
    }

    private static string StripAccents(string s) =>
        new string(s.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                        != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray())
        .Normalize(System.Text.NormalizationForm.FormC);

    private sealed record CityData(string Name, double Lat, double Lon);
}
