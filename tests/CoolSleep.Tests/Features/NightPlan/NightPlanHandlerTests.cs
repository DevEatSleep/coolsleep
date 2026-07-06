namespace CoolSleep.Tests.Features.NightPlan;

using CoolSleep.Api.Core;
using CoolSleep.Api.Features.NightPlan;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

public class NightPlanHandlerTests
{
    // Météo horaire complète (48 valeurs, forecast_days=2) telle que reçue du navigateur.
    private static List<double> FullTemps() =>
        Enumerable.Range(0, 48).Select(h => 30.0 - Math.Abs(h % 24 - 15) * 0.7).ToList();
    private static List<double> FullHumidity() =>
        Enumerable.Range(0, 48).Select(h => 50.0 + (h % 24) * 0.5).ToList();
    private static readonly List<string> SampleSunrise = ["2026-06-28T06:00", "2026-06-29T06:00"];

    private static NightPlanRequest Request(string city, HousingType housing) =>
        new(city, housing, FullTemps(), FullHumidity(), SampleSunrise);

    [Fact]
    public async Task HandleAsync_ReturnsCorrectCity()
    {
        var handler = CreateHandler();
        var result  = await handler.HandleAsync(Request("Paris", HousingType.AppartHaut));
        result.City.Should().Be("Paris");
    }

    [Fact]
    public async Task HandleAsync_ActionsNotEmpty()
    {
        var handler = CreateHandler();
        var result  = await handler.HandleAsync(Request("Lyon", HousingType.MaisonRdc));
        result.Actions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_RiskScoreBetween0And100()
    {
        var handler = CreateHandler();
        var result  = await handler.HandleAsync(Request("Nantes", HousingType.SousToits));
        result.RiskScore.Should().BeInRange(0, 100);
    }

    [Fact]
    public async Task HandleAsync_ActionsInEveningFirstOrder()
    {
        var handler = CreateHandler();
        var result  = await handler.HandleAsync(Request("Marseille", HousingType.AppartBas));
        result.Actions
            .Select(a => (a.Hour - 18 + 24) % 24)
            .Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task HandleAsync_MistralSucceeds_PopulatesLabelAndDetail()
    {
        var handler = CreateHandler(mistralEnabled: true, new FakeMistralClient());
        var result  = await handler.HandleAsync(Request("Paris", HousingType.AppartHaut));
        result.Actions.Should().Contain(a =>
            a.Label == $"Personalized: {a.MessageKey}" && a.Detail == "Personalized detail");
    }

    [Fact]
    public async Task HandleAsync_MistralThrows_LabelAndDetailStayNull()
    {
        var handler = CreateHandler(mistralEnabled: true, new FakeMistralClient(throwOnCall: true));
        var result  = await handler.HandleAsync(Request("Paris", HousingType.AppartHaut));
        result.Actions.Should().OnlyContain(a => a.Label == null && a.Detail == null);
    }

    [Fact]
    public async Task HandleAsync_MistralDisabled_SkipsCallAndLeavesNullFields()
    {
        var handler = CreateHandler(mistralEnabled: false, new FakeMistralClient(throwOnCall: true));
        var result  = await handler.HandleAsync(Request("Paris", HousingType.AppartHaut));
        result.Actions.Should().OnlyContain(a => a.Label == null && a.Detail == null);
    }

    // ── Factory ───────────────────────────────────────────────────────────
    private static NightPlanHandler CreateHandler(
        bool mistralEnabled = false, MistralClient? mistralClient = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mistral:Enabled"] = mistralEnabled.ToString()
            })
            .Build();
        return new NightPlanHandler(new FakeThermalClient(), mistralClient ?? new FakeMistralClient(), config);
    }
}

// ── Fake — méthode virtual sur le client concret ──────────────────────────
file sealed class FakeThermalClient()
    : ThermalClient(new HttpClient { BaseAddress = new Uri("http://localhost") })
{
    public override Task<ThermalResult> ComputeAsync(
        List<double> temps, List<double> daytimeTemps, List<double> humidity,
        HousingType housing, bool voletsFermes = true,
        double indoorTempStart = 24.0, bool debug = false, CancellationToken ct = default)
        => Task.FromResult(new ThermalResult(
            Hours: temps.Select((t, i) => new ThermalHour(
                (18 + i) % 24, t, t + 2,
                HeatIndexCalculator.Compute(t, humidity[i]),
                t + 2 - t,
                i > 4)).ToList(),
            OptimalOpenHour:    22,
            OptimalCloseHour:    4,
            MinIndoorReachable: 20.5,
            BaselineMinIndoor:  22.0,
            MinOutdoorHour:      3,
            MorningCloseHour:    6));
}

file sealed class FakeMistralClient(bool throwOnCall = false)
    : MistralClient(new HttpClient { BaseAddress = new Uri("http://localhost") },
        new ConfigurationBuilder().Build())
{
    public override Task<IReadOnlyList<PersonalizedAction>> PersonalizeAsync(
        string city, HousingType housing, IReadOnlyList<NightAction> actions,
        CancellationToken ct = default)
    {
        if (throwOnCall) throw new HttpRequestException("simulated Mistral outage");
        return Task.FromResult<IReadOnlyList<PersonalizedAction>>(
            actions.Select(a => new PersonalizedAction(
                a.Hour, a.MessageKey, $"Personalized: {a.MessageKey}", "Personalized detail")).ToList());
    }
}
