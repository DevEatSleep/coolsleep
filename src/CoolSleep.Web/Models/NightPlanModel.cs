namespace CoolSleep.Web.Models;

public sealed record NightPlanModel(
    string                          City,
    string                          RiskLevel,
    int                             RiskScore,
    double                          MinIndoorTemp,
    double                          BaselineMinIndoorTemp,
    double                          MinOutdoorTemp,
    int                             AvgHumidity,
    int?                            OptimalOpenHour,
    int?                            OptimalCloseHour,
    IReadOnlyList<NightActionModel> Actions);

public sealed record NightActionModel(
    int    Hour,
    string Label,
    string Detail,
    string ActionType);
