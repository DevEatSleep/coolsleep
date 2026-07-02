namespace CoolSleep.Api.Features.NightPlan;

using CoolSleep.Api.Core;

// Domaine — météo déjà récupérée côté navigateur (IP de l'utilisateur, quota dédié).
public sealed record NightPlanRequest(
    string       City,
    HousingType  Housing,
    List<double> HourlyTemps,
    List<double> HourlyHumidity,
    List<string> Sunrise,
    bool         VoletsFermes    = true,
    double       IndoorTempStart = 24.0,
    bool         Debug           = false);

// DTO de transport (housing en string, validé dans l'endpoint).
public sealed record NightPlanRequestBody(
    string       City,
    string       Housing,
    List<double> HourlyTemps,
    List<double> HourlyHumidity,
    List<string> Sunrise,
    bool         VoletsFermes    = true,
    double       IndoorTempStart = 24.0,
    bool         Debug           = false);
