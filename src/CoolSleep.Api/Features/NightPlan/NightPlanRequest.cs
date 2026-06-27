namespace CoolSleep.Api.Features.NightPlan;

using CoolSleep.Api.Core;

public sealed record NightPlanRequest(
    string      City,
    double      Lat,
    double      Lon,
    HousingType Housing,
    bool        VoletsFermes     = true,
    double      IndoorTempStart  = 24.0);
