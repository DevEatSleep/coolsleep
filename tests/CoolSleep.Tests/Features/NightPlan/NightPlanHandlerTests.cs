namespace CoolSleep.Tests.Features.NightPlan;

using CoolSleep.Api.Core;
using CoolSleep.Api.Features.NightPlan;
using FluentAssertions;

public class NightPlanHandlerTests
{
    private static readonly List<double> SampleTemps    = [33, 31, 29, 27, 25, 24, 23, 22, 21, 21, 20, 19, 19, 18, 18, 19];
    private static readonly List<double> SampleHumidity = [35, 38, 42, 46, 50, 53, 55, 57, 58, 59, 60, 61, 61, 62, 62, 60];

    [Fact]
    public async Task HandleAsync_ReturnsCorrectCity()
    {
        var handler = CreateHandler();
        var result  = await handler.HandleAsync(new("Paris", 48.85, 2.35, HousingType.AppartHaut));
        result.City.Should().Be("Paris");
    }

    [Fact]
    public async Task HandleAsync_ActionsNotEmpty()
    {
        var handler = CreateHandler();
        var result  = await handler.HandleAsync(new("Lyon", 45.75, 4.83, HousingType.MaisonRdC));
        result.Actions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_RiskScoreBetween0And100()
    {
        var handler = CreateHandler();
        var result  = await handler.HandleAsync(new("Nantes", 47.21, -1.55, HousingType.SousToits));
        result.RiskScore.Should().BeInRange(0, 100);
    }

    [Fact]
    public async Task HandleAsync_ActionsInEveningFirstOrder()
    {
        var handler = CreateHandler();
        var result  = await handler.HandleAsync(new("Marseille", 43.30, 5.37, HousingType.AppartBas));
        result.Actions
            .Select(a => (a.Hour - 18 + 24) % 24)
            .Should().BeInAscendingOrder();
    }

    // ── Factory ───────────────────────────────────────────────────────────
    private static NightPlanHandler CreateHandler() =>
        new(new FakeOpenMeteoClient(), new FakeThermalClient());
}

// ── Fakes — méthodes virtual sur les clients concrets ─────────────────────
file sealed class FakeOpenMeteoClient() : OpenMeteoClient(new HttpClient())
{
    public override Task<(List<double> Temps, List<double> DaytimeTemps, List<double> Humidity, TimeOnly Sunrise)> GetNightForecastAsync(
        double lat, double lon, CancellationToken ct = default)
        => Task.FromResult((
            new List<double> { 33, 31, 29, 27, 25, 24, 23, 22, 21, 21, 20, 19, 19, 18, 18, 19 },
            new List<double> { 28, 30, 33, 35, 37, 38, 37, 35 },
            new List<double> { 35, 38, 42, 46, 50, 53, 55, 57, 58, 59, 60, 61, 61, 62, 62, 60 },
            new TimeOnly(6, 15)
        ));
}

file sealed class FakeThermalClient()
    : ThermalClient(new HttpClient { BaseAddress = new Uri("http://localhost") })
{
    public override Task<ThermalResult> ComputeAsync(
        List<double> temps, List<double> daytimeTemps, List<double> humidity,
        HousingType housing, double indoorTempStart = 24.0,
        bool voletsFermes = true, CancellationToken ct = default)
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
            MorningCloseHour:    6,
            GainVsBaseline:      1.5));
}
