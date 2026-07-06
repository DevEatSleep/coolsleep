namespace CoolSleep.Web.Services;

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;

public class StringsService(NavigationManager nav) : IAsyncInitialize
{
    private JsonElement _root;
    private bool _initialized = false;

    public event Action? OnInitialized;
    public bool IsReady => _initialized;

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        try
        {
            using var http = new HttpClient();
            var baseUri = nav.BaseUri;
            var jsonUri = new Uri(baseUri + "i18n/fr.json");
            using var doc = await http.GetFromJsonAsync<JsonDocument>(jsonUri);
            _root = doc!.RootElement.Clone();
            _initialized = true;
        }
        finally
        {
            OnInitialized?.Invoke();
        }
    }

    public string Get(string section, string key)
    {
        if (!_initialized) return "";

        try
        {
            return _root.GetProperty(section).GetProperty(key).GetString() ?? key;
        }
        catch
        {
            return $"[{section}.{key}]";
        }
    }

    public string ActionLabel(string messageKey, IReadOnlyDictionary<string, double>? @params = null)
    {
        if (!_initialized) return "";

        try
        {
            var template = _root.GetProperty("actions").GetProperty(messageKey).GetProperty("label").GetString();
            return template is null ? messageKey : Interpolate(template, @params);
        }
        catch
        {
            return $"[actions.{messageKey}.label]";
        }
    }

    public string ActionDetail(string messageKey, IReadOnlyDictionary<string, double>? @params = null)
    {
        if (!_initialized) return "";

        try
        {
            var template = _root.GetProperty("actions").GetProperty(messageKey).GetProperty("detail").GetString();
            return template is null ? messageKey : Interpolate(template, @params);
        }
        catch
        {
            return $"[actions.{messageKey}.detail]";
        }
    }

    private static string Interpolate(string template, IReadOnlyDictionary<string, double>? @params)
    {
        if (@params is null || @params.Count == 0) return template;
        foreach (var (k, v) in @params)
            template = template.Replace($"{{{k}}}", ((int)Math.Round(v)).ToString());
        return template;
    }
}

public interface IAsyncInitialize
{
    Task InitializeAsync();
}
