namespace CoolSleep.Api.Core;

/// <summary>
/// Génère la liste d'actions horaires à partir des données thermiques.
/// Les données thermiques (T° intérieure, heures optimales) viennent du service Python.
/// </summary>
public static class NightPlanEngine
{
    /// <summary>
    /// T° extérieure absolue maximale au-dessus de laquelle ouvrir les fenêtres
    /// n'apporte pas de rafraîchissement ressenti, même si T_ext &lt; T_int.
    /// </summary>
    private const double MaxUsableOutdoorTemp = 32.0;

    /// <summary>
    /// Marge au-dessus du minimum nocturne pour considérer que T_ext "remonte vraiment".
    /// Évite les faux positifs sur une courbe plate.
    /// </summary>
    private const double ExtRisingMargin = 0.5;

    /// <summary>
    /// Plage horaire "nocturne" pour le rafraîchissement au creux.
    /// En dehors de cette plage, le creux est matinal → pas d'action réveille-matin.
    /// </summary>
    private const int NocturnalStart = 22;
    private const int NocturnalEnd   = 5;

    public static NightPlan Build(
        string                    city,
        IReadOnlyList<HourlyData> hours,
        int?                      optimalOpenHour,
        int?                      optimalCloseHour,
        double                    minIndoorTemp,
        HousingType               housing          = HousingType.AppartBas,
        TimeOnly?                 sunrise          = null,
        int                       minOutdoorHour   = 2,
        int?                      morningCloseHour = 6,
        bool                      voletsFermes     = true)
    {
        var effectiveMorningClose = CapMorningCloseToSunrise(morningCloseHour, sunrise);

        var actions   = BuildActions(hours, housing, optimalOpenHour, optimalCloseHour,
                                     minOutdoorHour, effectiveMorningClose, voletsFermes);
        var riskScore = ComputeRiskScore(hours);
        var riskLevel = ClassifyRisk(riskScore);

        return new NightPlan(city, riskLevel, riskScore, minIndoorTemp,
                             optimalOpenHour, optimalCloseHour, actions);
    }

    private static int? CapMorningCloseToSunrise(int? morningCloseHour, TimeOnly? sunrise)
    {
        if (morningCloseHour is null || sunrise is null) return morningCloseHour;
        var cap = sunrise.Value.Hour == 0 ? 23 : sunrise.Value.Hour - 1;
        return Math.Min(morningCloseHour.Value, cap);
    }

    private static IReadOnlyList<NightAction> BuildActions(
        IReadOnlyList<HourlyData> hours,
        HousingType               housing,
        int?                      openHour,
        int?                      closeHour,
        int                       minOutdoorHour,
        int?                      morningCloseHour,
        bool                      voletsFermes)
    {
        var actions            = new List<NightAction>();
        var currentHour        = DateTime.Now.Hour;
        var windowWasOpened    = false;
        var passiveCardEmitted = false;

        // ── 18h : fermer les volets ─────────────────────────────────────────
        var h18 = hours.FirstOrDefault(h => h.Hour == 18);
        if (!voletsFermes
            && currentHour < 18
            && (h18 is null || !h18.OpenWindowRecommended))
        {
            actions.Add(new(18,
                "FermerVolets",
                new Dictionary<string, double>
                {
                    ["outdoorTemp"] = h18?.OutdoorTemp ?? 0,
                    ["indoorTemp"] = h18?.IndoorTempEstimated ?? 0
                },
                ActionType.FermerVolets));
        }

        // ── Cas climatisé ───────────────────────────────────────────────────
        if (housing == HousingType.Climatise)
        {
            actions.Add(new(20,
                "ReglerClimatisation",
                new Dictionary<string, double>(),
                ActionType.ReglerClimatisation));
            return [.. actions];
        }

        // ── Heure d'ouverture optimale ──────────────────────────────────────
        if (openHour is not null)
        {
            var hOpen       = hours.FirstOrDefault(h => h.Hour == openHour);
            var outdoorTemp = hOpen?.OutdoorTemp ?? 0;

            if (outdoorTemp <= MaxUsableOutdoorTemp)
            {
                windowWasOpened = true;
                actions.Add(new(openHour.Value,
                    "OuvrirFenetres",
                    new Dictionary<string, double>
                    {
                        ["outdoorTemp"] = outdoorTemp,
                        ["indoorTemp"] = hOpen?.IndoorTempEstimated ?? 0
                    },
                    ActionType.OuvrirFenetres));
            }
            else
            {
                passiveCardEmitted = true;
                actions.Add(new(openHour.Value,
                    "NuitSansFraicheur",
                    new Dictionary<string, double>
                    {
                        ["outdoorTemp"] = outdoorTemp
                    },
                    ActionType.InformationSeulement));
            }
        }

        // ── Heure de fermeture optimale ─────────────────────────────────────
        // Check if morning close comes before regular close (will add FermerMatin instead)
        var morningIsBeforeClose = morningCloseHour is null || closeHour is null
            || NightHour(morningCloseHour.Value) < NightHour(closeHour.Value);

        if (closeHour is not null && windowWasOpened
            && !(morningCloseHour is not null && morningCloseHour != closeHour && morningIsBeforeClose))
        {
            var hClose  = hours.FirstOrDefault(h => h.Hour == closeHour);

            // FIX : T_ext "remonte" ssi elle dépasse le minimum observé dans la fenêtre
            // ouverte + marge. Comparer à T_ext à openHour était fragile (courbe plate).
            var minExtInWindow = hours
                .Where(h =>
                {
                    var nh = NightHour(h.Hour);
                    return openHour is not null
                        && nh >= NightHour(openHour.Value)
                        && nh <= NightHour(closeHour.Value);
                })
                .Select(h => h.OutdoorTemp)
                .DefaultIfEmpty(hClose?.OutdoorTemp ?? 0)
                .Min();

            var extIsRising = hClose is not null
                           && hClose.OutdoorTemp > minExtInWindow + ExtRisingMargin;

            var messageKey = extIsRising ? "FermerFenetres_Rising" : "FermerFenetres_Equalized";

            actions.Add(new(closeHour.Value,
                messageKey,
                new Dictionary<string, double>
                {
                    ["outdoorTemp"] = hClose?.OutdoorTemp ?? 0
                },
                ActionType.FermerFenetres));
        }

        // ── Creux nocturne ──────────────────────────────────────────────────
        var atMin          = hours.FirstOrDefault(h => h.Hour == minOutdoorHour);
        var isNocturnal    = minOutdoorHour >= NocturnalStart || minOutdoorHour <= NocturnalEnd;
        var conflictsClose = minOutdoorHour == closeHour;
        var creusTempOk    = atMin is null || atMin.OutdoorTemp <= MaxUsableOutdoorTemp;
        var cycleComplet   = windowWasOpened && closeHour is not null;

        if (!conflictsClose && isNocturnal && creusTempOk && windowWasOpened
            && (atMin is null || atMin.OpenWindowRecommended))
        {
            actions.Add(new(minOutdoorHour,
                "RefraichissementNocturne",
                new Dictionary<string, double>
                {
                    ["alarmHour"] = minOutdoorHour,
                    ["outdoorTemp"] = atMin?.OutdoorTemp ?? 0
                },
                ActionType.RefraichissementNocturne));
        }
        else if (!isNocturnal && atMin is not null && !passiveCardEmitted && !cycleComplet)
        {
            passiveCardEmitted = true;
            actions.Add(new(minOutdoorHour,
                "NuitSansCreux",
                new Dictionary<string, double>
                {
                    ["outdoorTemp"] = atMin.OutdoorTemp,
                    ["alarmHour"] = minOutdoorHour
                },
                ActionType.InformationSeulement));
        }

        // ── Fermeture matinale ──────────────────────────────────────────────
        if (morningCloseHour is not null
            && morningCloseHour != closeHour
            && morningIsBeforeClose
            && windowWasOpened)
        {
            var hMorn = hours.FirstOrDefault(h => h.Hour == morningCloseHour);

            actions.Add(new(morningCloseHour.Value,
                "FermerMatin",
                new Dictionary<string, double>
                {
                    ["outdoorTemp"] = hMorn?.OutdoorTemp ?? 0
                },
                ActionType.FermerMatin));
        }

        // ── Fallback plan vide ──────────────────────────────────────────────
        var hasActionableCard = actions.Any(a =>
            a.ActionType != ActionType.FermerVolets &&
            a.ActionType != ActionType.InformationSeulement);

        if (!hasActionableCard && !passiveCardEmitted)
        {
            actions.Add(new(20,
                "AucuneAction",
                new Dictionary<string, double>(),
                ActionType.InformationSeulement));
        }

        return [.. actions.OrderBy(a => NightHour(a.Hour))];
    }

    /// <summary>
    /// Normalise une heure sur l'axe nocturne 18h→35h (18→18, 0→24, 9→33).
    /// Permet de comparer et trier correctement les actions d'une même nuit.
    /// </summary>
    private static int NightHour(int hour)
        => hour >= 18 ? hour : hour + 24;

    private static int ComputeRiskScore(IReadOnlyList<HourlyData> hours)
    {
        if (hours.Count == 0) return 0;

        var hi    = hours.Max(h => h.HeatIndex);
        var score = hi switch
        {
            < 27 => Lerp(0,  27, 0,  25, hi),
            < 32 => Lerp(27, 32, 26, 50, hi),
            < 38 => Lerp(32, 38, 51, 75, hi),
            < 44 => Lerp(38, 44, 76, 99, hi),
            _    => 100.0
        };
        return (int)Math.Round(Math.Clamp(score, 0, 100));
    }

    private static double Lerp(
        double inMin,  double inMax,
        double outMin, double outMax,
        double value)
        => outMin + (value - inMin) / (inMax - inMin) * (outMax - outMin);

    private static RiskLevel ClassifyRisk(int score) => score switch
    {
        <= 25 => RiskLevel.Normal,
        <= 50 => RiskLevel.Modere,
        <= 75 => RiskLevel.Eleve,
        _     => RiskLevel.Critique
    };
}

// ── Records & Enums ────────────────────────────────────────────────────────

public sealed record NightPlan(
    string                     City,
    RiskLevel                  RiskLevel,
    int                        RiskScore,
    double                     MinIndoorTemp,
    int?                       OptimalOpenHour,
    int?                       OptimalCloseHour,
    IReadOnlyList<NightAction> Actions);

public sealed record NightAction(
    int                                   Hour,
    string                                MessageKey,
    IReadOnlyDictionary<string, double>   Params,
    ActionType                            ActionType);

public sealed record HourlyData(
    int    Hour,
    double OutdoorTemp,
    double IndoorTempEstimated,
    double HeatIndex,
    bool   OpenWindowRecommended);

