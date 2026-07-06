namespace CoolSleep.Api.Features.NightPlan;

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoolSleep.Api.Core;

/// <summary>
/// Rephrase les messages d'action via l'API Mistral pour les rendre uniques et
/// personnalisés (ville + type de logement). Appel best-effort : une seule tentative,
/// sans retry — l'appelant (NightPlanHandler) doit intercepter tout échec et retomber
/// sur les templates statiques i18n plutôt que de faire échouer la requête.
/// </summary>
public class MistralClient(HttpClient http, IConfiguration config)
{
    public virtual async Task<IReadOnlyList<PersonalizedAction>> PersonalizeAsync(
        string                     city,
        HousingType                housing,
        IReadOnlyList<NightAction> actions,
        CancellationToken          ct = default)
    {
        var payload = new
        {
            model = config["Mistral:Model"] ?? "mistral-small-latest",
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user",   content = BuildUserPrompt(city, housing, actions) }
            },
            response_format = new { type = "json_object" }
        };

        var response = await http.PostAsJsonAsync("/v1/chat/completions", payload, ct);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadFromJsonAsync<MistralChatResponse>(ct)
                  ?? throw new InvalidOperationException("Mistral returned null");

        return ParseActions(raw);
    }

    private const string SystemPrompt =
        "Tu reformules des messages d'un plan de rafraîchissement nocturne pour une " +
        "application française d'aide contre les canicules. Pour chaque action reçue, " +
        "réécris 'label' (court, à l'impératif) et 'detail' (une phrase) en tenant compte " +
        "de la ville et du type de logement pour rendre le message unique et personnalisé. " +
        "essaye d'être original et créatif, mais reste concis et clair. " +
        "Conserve exactement les valeurs numériques fournies (températures, heures) sans " +
        "les modifier. Ne rajoute et ne supprime aucune action. Réponds strictement en JSON " +
        "avec la forme : {\"actions\": [{\"hour\": int, \"messageKey\": string, \"label\": string, \"detail\": string}]}.";

    private static string BuildUserPrompt(
        string city, HousingType housing, IReadOnlyList<NightAction> actions)
    {
        var payload = new
        {
            city,
            housing = housing.ToPromptDescription(),
            actions = actions.Select(a => new
            {
                hour       = a.Hour,
                messageKey = a.MessageKey,
                @params    = a.Params
            })
        };
        return JsonSerializer.Serialize(payload);
    }

    private static IReadOnlyList<PersonalizedAction> ParseActions(MistralChatResponse raw)
    {
        var content = raw.Choices.FirstOrDefault()?.Message.Content
                      ?? throw new InvalidOperationException("Mistral response has no content");
        var parsed = JsonSerializer.Deserialize<MistralActionsPayload>(content,
                         new JsonSerializerOptions(JsonSerializerDefaults.Web))
                     ?? throw new InvalidOperationException("Mistral content is not valid JSON");
        return parsed.Actions;
    }
}

public sealed record PersonalizedAction(int Hour, string MessageKey, string Label, string Detail);

// ── DTOs pour l'API de complétion de chat Mistral (compatible OpenAI) ──────────────
public sealed record MistralChatResponse(
    [property: JsonPropertyName("choices")] List<MistralChoice> Choices);

public sealed record MistralChoice(
    [property: JsonPropertyName("message")] MistralMessage Message);

public sealed record MistralMessage(
    [property: JsonPropertyName("content")] string Content);

// ── Forme attendue du JSON renvoyé dans MistralMessage.Content ─────────────────────
public sealed record MistralActionsPayload(List<PersonalizedAction> Actions);
