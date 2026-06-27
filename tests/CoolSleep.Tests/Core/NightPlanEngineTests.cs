namespace CoolSleep.Tests.Core;

using CoolSleep.Api.Core;
using FluentAssertions;

public class NightPlanEngineTests
{
    private static readonly List<HourlyData> SampleHours =
    [
        new(18, 33, 30, 38, false),
        new(20, 29, 31, 33, false),
        new(22, 25, 29, 27, true),
        new(00, 22, 27, 23, true),
        new(02, 20, 25, 21, true),
        new(06, 18, 22, 19, true),
    ];

    [Fact]
    public void Build_ReturnsCorrectCity()
    {
        var plan = NightPlanEngine.Build("Paris", SampleHours, 22, 4, 21.5);
        plan.City.Should().Be("Paris");
    }

    [Fact]
    public void Build_ActionsAreInEveningFirstOrder()
    {
        var plan = NightPlanEngine.Build("Lyon", SampleHours, 22, 4, 21.5);
        plan.Actions
            .Select(a => (a.Hour - 18 + 24) % 24)
            .Should().BeInAscendingOrder();
    }

    [Fact]
    public void Build_ContainsFermerVolets_WhenHotAt18h_AndVoletsOuverts()
    {
        // voletsFermes=false + T_ext > T_int à 18h → action FermerVolets requise
        var plan = NightPlanEngine.Build("Nantes", SampleHours, null, null, 22.0,
            voletsFermes: false);
        plan.Actions.Should().Contain(a =>
            a.Hour == 18 && a.ActionType == ActionType.FermerVolets);
    }

    [Fact]
    public void Build_OmitsFermerVolets_WhenAlreadyCoolAt18h()
    {
        // T_ext(18h) < T_int(18h) → OpenWindowRecommended=true → pas besoin de fermer même si volets ouverts
        var coolStart = SampleHours
            .Select(h => h.Hour == 18 ? h with { OpenWindowRecommended = true } : h)
            .ToList();
        var plan = NightPlanEngine.Build("Nantes", coolStart, 18, null, 22.0,
            voletsFermes: false);
        plan.Actions.Should().NotContain(a => a.ActionType == ActionType.FermerVolets);
    }

    [Fact]
    public void Build_NoFermerVolets_WhenVoletsFermes()
    {
        // voletsFermes=true (défaut) → action FermerVolets jamais présente
        var plan = NightPlanEngine.Build("Nantes", SampleHours, null, null, 22.0,
            voletsFermes: true);
        plan.Actions.Should().NotContain(a => a.ActionType == ActionType.FermerVolets);
    }

    [Fact]
    public void Build_HasFermerVolets_WhenVoletsOuverts_AndHotOutside()
    {
        // voletsFermes=false + nuit chaude → action FermerVolets présente
        var plan = NightPlanEngine.Build("Nantes", SampleHours, 22, 4, 22.0,
            voletsFermes: false);
        plan.Actions.Should().Contain(a => a.ActionType == ActionType.FermerVolets);
    }

    [Fact]
    public void Build_RiskLevelEleveForHighHeatIndex()
    {
        // HeatIndex 35 → score interpolé dans [51,75] → Élevé
        var hotNight = SampleHours.Select(h => h with { HeatIndex = 35 }).ToList();
        var plan     = NightPlanEngine.Build("Marseille", hotNight, 22, 3, 25.0);
        plan.RiskLevel.Should().Be(RiskLevel.Eleve);
    }

    [Fact]
    public void Build_NoOpenWindowAction_WhenOpenHourIsNull()
    {
        var plan = NightPlanEngine.Build("Bordeaux", SampleHours, null, null, 24.0);
        plan.Actions.Should().NotContain(a => a.ActionType == ActionType.OuvrirFenetres);
    }

    [Fact]
    public void Build_Climatise_OnlyVoletsAndClimAction()
    {
        var plan = NightPlanEngine.Build("Nice", SampleHours, 22, 4, 25.0,
            HousingType.Climatise);
        plan.Actions.Should().NotContain(a => a.ActionType == ActionType.OuvrirFenetres);
        plan.Actions.Should().NotContain(a => a.ActionType == ActionType.OuvrirMatin);
        plan.Actions.Should().Contain(a => a.ActionType == ActionType.VentilateurOn);
    }

    [Fact]
    public void Build_UsesMinOutdoorHour_ForNocturnalRefresh()
    {
        // minOutdoorHour=3 → l'action de rafraîchissement doit être à 3h, pas à 2h
        var plan = NightPlanEngine.Build("Toulouse", SampleHours, 22, 4, 25.0,
            minOutdoorHour: 3);
        plan.Actions.Should().Contain(a =>
            a.Hour == 3 && a.ActionType == ActionType.RefraichissementNocturne);
        plan.Actions.Should().NotContain(a =>
            a.Hour == 2 && a.ActionType == ActionType.RefraichissementNocturne);
    }

    [Fact]
    public void Build_NoNocturnalRefresh_WhenOutdoorWarmerThanIndoorAtMinHour()
    {
        // T_ext > T_int à l'heure du creux → OpenWindowRecommended=false → pas d'action
        var warmMin = SampleHours
            .Select(h => h.Hour == 2 ? h with { OpenWindowRecommended = false } : h)
            .ToList();
        var plan = NightPlanEngine.Build("Toulouse", warmMin, 22, 4, 25.0,
            minOutdoorHour: 2);
        plan.Actions.Should().NotContain(a => a.ActionType == ActionType.RefraichissementNocturne);
    }

    [Fact]
    public void Build_UsesMorningOpenHour_ForOuvrirMatin()
    {
        // morningCloseHour=5 → l'action OuvrirMatin doit être à 5h
        var plan = NightPlanEngine.Build("Bordeaux", SampleHours, 22, 4, 21.5,
            morningCloseHour: 5);
        plan.Actions.Should().Contain(a =>
            a.Hour == 5 && a.ActionType == ActionType.OuvrirMatin);
    }

    [Fact]
    public void Build_NoOuvrirMatin_WhenMorningOpenHourIsNull()
    {
        // morningCloseHour=null → matin trop chaud, pas d'action
        var plan = NightPlanEngine.Build("Bordeaux", SampleHours, 22, 4, 21.5,
            morningCloseHour: null);
        plan.Actions.Should().NotContain(a => a.ActionType == ActionType.OuvrirMatin);
    }

    [Fact]
    public void Build_NoOuvrirMatin_WhenMorningOpenHourEqualsCloseHour()
    {
        // morningCloseHour == closeHour → action déjà couverte par FermerFenetres, pas de doublon
        var plan = NightPlanEngine.Build("Bordeaux", SampleHours, 22, 4, 21.5,
            morningCloseHour: 4);
        plan.Actions.Should().NotContain(a => a.ActionType == ActionType.OuvrirMatin);
    }
}
