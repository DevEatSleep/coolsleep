namespace CoolSleep.Api.Features.NightPlan;

public sealed record NightPlanResponse(
    string                             City,
    string                             RiskLevel,
    int                                RiskScore,
    double                             MinIndoorTemp,
    double                             BaselineMinIndoorTemp,
    double                             MinOutdoorTemp,
    int                                AvgHumidity,
    int?                               OptimalOpenHour,
    int?                               OptimalCloseHour,
    IReadOnlyList<NightActionResponse> Actions);

public sealed record NightActionResponse(
    int    Hour,
    string Label,
    string Detail,
    string ActionType);
