import numpy as np
from .schemas import HousingType, ThermalRequest, ThermalResponse, HourlyThermal

# ── Constantes physiques par type de logement ──────────────────────────────
#
# alpha : inertie thermique — fraction de T_int(h-1) conservée à chaque heure.
#   0.0 = aucune inertie (climatisé, suit T_ext immédiatement)
#   0.9 = très forte inertie (sous-toits, lente à chauffer ET à refroidir)
#
# lag   : décalage horaire entre variation T_ext et impact mesurable sur T_int.
#   Dépend de l'épaisseur et conductivité des parois.
#
# alpha_volets_bonus : gain d'inertie apporté par les volets fermés en journée.
#   Physiquement : réduction des apports radiatifs solaires.
#   Valeur différenciée par type plutôt qu'un +0.35 uniforme.

THERMAL_INERTIA: dict[HousingType, float] = {
    HousingType.climatise:         0.0,
    HousingType.maison_rdc:        0.15,
    HousingType.maison_etage:      0.28,
    HousingType.maison_sous_toits: 0.38,
    HousingType.appart_bas:        0.25,
    HousingType.appart_haut:       0.40,
    HousingType.sous_toits:        0.55,
}

LAG_HOURS: dict[HousingType, int] = {
    HousingType.climatise:         0,
    HousingType.maison_rdc:        2,
    HousingType.maison_etage:      3,
    HousingType.maison_sous_toits: 4,
    HousingType.appart_bas:        3,
    HousingType.appart_haut:       4,
    HousingType.sous_toits:        5,
}

# Bonus d'inertie volets fermés : physiquement borné et différencié par type.
# Un RDC bénéficie peu des volets (parois légères) ; un sous-toits bénéficie
# surtout de la réduction du rayonnement solaire direct sur la toiture.
VOLETS_ALPHA_BONUS: dict[HousingType, float] = {
    HousingType.climatise:         0.00,
    HousingType.maison_rdc:        0.10,
    HousingType.maison_etage:      0.15,
    HousingType.maison_sous_toits: 0.20,
    HousingType.appart_bas:        0.12,
    HousingType.appart_haut:       0.18,
    HousingType.sous_toits:        0.22,
}

START_HOUR     = 18
DELTA_OPEN_MIN = 1.5  # seuil minimal T_int - T_ext pour recommander l'ouverture (°C)


# ── Physique ────────────────────────────────────────────────────────────────

def _heat_index(t: float, rh: float) -> float:
    """
    Indice de chaleur Steadman/NOAA.
    Valide pour T > 27°C et RH > 40% — retourne T brute sinon.
    """
    if t < 27:
        return t
    return (
        -8.784
        + 1.611    * t
        + 2.338    * rh
        - 0.146    * t  * rh
        - 0.0123   * t**2
        - 0.0164   * rh**2
        + 0.00221  * t**2 * rh
        + 0.00072  * t   * rh**2
        - 0.000003582 * t**2 * rh**2
    )


def _compute_alpha_warmup(alpha: float, housing: HousingType, volets_fermes: bool) -> float:
    """
    Alpha effectif pendant la journée (warmup).
    Les volets fermés augmentent l'inertie de façon différenciée et physiquement bornée.
    """
    bonus = VOLETS_ALPHA_BONUS[housing] if volets_fermes else 0.0
    return min(alpha + bonus, 0.92)


def _warmup(
    daytime_temps: list[float],
    t_start: float,
    alpha_warmup: float,
) -> float:
    """
    Simule la chauffe diurne (10h–17h) pour estimer T_int à 18h.
    Utilise les températures réelles de la journée plutôt que temps[0].
    """
    t = t_start
    for t_ext in daytime_temps:
        t = alpha_warmup * t + (1 - alpha_warmup) * t_ext
    return t


def _simulate(
    temps: np.ndarray,
    t_init: float,
    alpha: float,
    lag: int,
) -> np.ndarray:
    """
    Modèle RC discret du premier ordre (fenêtres toujours fermées) :
        T_int(h) = α · T_int(h-1) + (1-α) · T_ext(h-lag)
    """
    n = len(temps)
    indoor = np.zeros(n)
    indoor[0] = t_init
    for i in range(1, n):
        ext_idx = max(0, i - lag)
        indoor[i] = alpha * indoor[i - 1] + (1 - alpha) * temps[ext_idx]
    return indoor


def _simulate_ventilated(
    temps: np.ndarray,
    t_init: float,
    alpha: float,
    lag: int,
) -> np.ndarray:
    """
    RC + terme de ventilation quand T_int(h-1) - T_ext(h-1) ≥ DELTA_OPEN_MIN.
    Fenêtres ouvertes : échange direct (lag=0, alpha_vent = max(alpha*0.3, 0.05)).
    Fenêtres fermées  : RC nominal (alpha, lag).
    """
    alpha_vent = max(alpha * 0.3, 0.05)
    n = len(temps)
    indoor = np.zeros(n)
    indoor[0] = t_init
    for i in range(1, n):
        if indoor[i - 1] - temps[i - 1] >= DELTA_OPEN_MIN:
            indoor[i] = alpha_vent * indoor[i - 1] + (1 - alpha_vent) * temps[i]
        else:
            ext_idx = max(0, i - lag)
            indoor[i] = alpha * indoor[i - 1] + (1 - alpha) * temps[ext_idx]
    return indoor


# ── Point d'entrée principal ────────────────────────────────────────────────

def compute_indoor_temps(req: ThermalRequest) -> ThermalResponse:
    """
    Calcule les températures intérieures heure par heure (18h → 9h)
    et dérive les recommandations d'ouverture/fermeture de fenêtres.

    Stratégie optimale  : fenêtres ouvertes quand T_ext < T_int - delta_min.
    Baseline            : fenêtres toujours fermées (RC pur, sans ventilation).
    """
    alpha = THERMAL_INERTIA[req.housing]
    lag   = LAG_HOURS[req.housing]
    temps = np.array(req.hourly_temps)
    rh    = np.array(req.hourly_humidity)
    n     = len(temps)
    
    if req.debug:
        print("\n=== DEBUG: Thermal Calculation ===")
        print(f"Housing: {req.housing.value}")
        print(f"Alpha (inertia): {alpha}, Lag: {lag}h")
        print(f"Volets fermés (warmup): {req.volets_fermes}")

    # ── 1. Warmup diurne avec températures réelles 10h–17h ─────────────────
    # Cas climatisé : T_int est pilotée par le thermostat, le warmup ne s'applique pas.
    if req.housing == HousingType.climatise:
        t_at_18h = req.indoor_temp_start
    else:
        alpha_warmup = _compute_alpha_warmup(alpha, req.housing, req.volets_fermes)
        t_at_18h     = _warmup(req.daytime_temps, req.indoor_temp_start, alpha_warmup)

    # ── 2. Simulation nocturne — stratégie optimale (avec ventilation) ──────
    indoor = _simulate_ventilated(temps, t_at_18h, alpha, lag)

    # ── 3. Baseline — fenêtres toujours fermées (RC pur, sans ventilation) ──
    indoor_baseline = _simulate(temps, t_at_18h, alpha, lag)
    
    if req.debug:
        print(f"\nT_int at 18h: {round(t_at_18h, 1)}°C")
        print(f"\n{'Hour':<6} {'Time':<8} {'T_out':<8} {'T_in':<8} {'T_base':<8} {'Delta':<8} {'RH':<6} {'HI':<8}")
        print("-" * 70)
        for i in range(n):
            hour = (START_HOUR + i) % 24
            delta = indoor[i] - temps[i]
            hi = _heat_index(float(temps[i]), float(rh[i]))
            open_rec = "OPEN" if delta >= DELTA_OPEN_MIN else ""
            print(f"{i:<6} {hour:02d}:00    {temps[i]:<8.1f} {indoor[i]:<8.1f} {indoor_baseline[i]:<8.1f} {delta:<8.1f} {rh[i]:<6.1f} {hi:<8.1f} {open_rec}")
        print("-" * 70)
        print(f"Min indoor (ventilated): {round(float(indoor.min()), 1)}°C")
        print(f"Min indoor (baseline):   {round(float(indoor_baseline.min()), 1)}°C")
        print(f"Gain vs baseline: {round(float(indoor_baseline.min()) - float(indoor.min()), 1)}°C\n")

    # ── 4. Construction des données horaires ────────────────────────────────
    hours_data: list[HourlyThermal] = [
        HourlyThermal(
            hour=(START_HOUR + i) % 24,
            outdoor_temp=round(float(temps[i]), 1),
            indoor_temp_estimated=round(float(indoor[i]), 1),
            heat_index=round(_heat_index(float(temps[i]), float(rh[i])), 1),
            delta=round(float(indoor[i] - temps[i]), 1),
            # Seuil minimal pour éviter les recommandations pour des écarts négligeables
            open_window_recommended=bool(indoor[i] - temps[i] >= DELTA_OPEN_MIN),
        )
        for i in range(n)
    ]

    # ── 5. Agrégats ─────────────────────────────────────────────────────────
    optimal_open_hour  = next((h.hour for h in hours_data if h.open_window_recommended), None)
    optimal_close_hour = next((h.hour for h in reversed(hours_data) if h.open_window_recommended), None)

    min_idx          = int(np.argmin(temps))
    min_outdoor_hour = (START_HOUR + min_idx) % 24

    # FIX : si le creux absolu tombe après optimal_close_hour dans la nuit,
    # il est hors de la fenêtre d'action — l'utilisateur a déjà fermé.
    # On neutralise en remontant min_outdoor_hour à optimal_close_hour,
    # ce qui déclenchera le guard conflictsClose dans NightPlanEngine
    # et évitera d'émettre une action réveille-matin incohérente.
    if optimal_close_hour is not None:
        close_idx = next(
            (i for i, h in enumerate(hours_data) if h.hour == optimal_close_hour),
            None,
        )
        if close_idx is not None and min_idx > close_idx:
            min_outdoor_hour = optimal_close_hour  # neutralise le creux post-fermeture

    # Dernière heure fraîche entre 0h et 9h → moment limite pour fermer avant le réchauffement
    morning_hours      = [h for h in hours_data if 0 <= h.hour <= 9]
    morning_close_hour = next(
        (h.hour for h in reversed(morning_hours) if h.open_window_recommended),
        None,
    )

    min_indoor     = round(float(indoor.min()), 1)
    baseline_min   = round(float(indoor_baseline.min()), 1)
    gain           = round(baseline_min - min_indoor, 1)  # positif = stratégie gagnante

    return ThermalResponse(
        hours=hours_data,
        optimal_open_hour=optimal_open_hour,
        optimal_close_hour=optimal_close_hour,
        morning_close_hour=morning_close_hour,
        min_indoor_reachable=min_indoor,
        baseline_min_indoor=baseline_min,
        min_outdoor_hour=min_outdoor_hour,
        gain_vs_baseline=gain,
    )
