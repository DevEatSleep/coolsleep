import numpy as np
from .schemas import HousingType, ThermalRequest, ThermalResponse, HourlyThermal

# ═══════════════════════════════════════════════════════════════════════════
#  MODÈLE THERMIQUE 1R1C — version recalibrée
#
#  Corrections vs. version précédente :
#
#  1. INERTIE (alpha) recalibrée sur des constantes de temps physiques.
#     Dans un RC du premier ordre : alpha = exp(-Δt/τ), avec Δt = 1 h.
#     Un logement réel a τ ∈ [6 h, 25 h] → alpha ∈ [0.85, 0.96].
#     Les anciennes valeurs (0.15–0.55) correspondaient à τ ≈ 30 min–1 h,
#     soit une tente, pas un bâtiment. C'était la cause des +5°C/h absurdes.
#
#  2. LAG supprimé. Le déphasage entre T_ext et T_int est une propriété
#     ÉMERGENTE de τ, pas un paramètre indépendant. Empiler un lag explicite
#     sur un alpha bas double-comptait le retard et agissait comme un barrage :
#     les `lag` premières heures restaient plates, puis le pic diurne de 18h
#     débarquait d'un coup à (18h + lag) avec un poids (1-alpha). D'où le spike
#     26.3 → 31.4°C observé. Avec un τ correct, plus besoin de lag.
#
#  3. WARMUP unifié sur la même physique. Plus de table WARMUP_INERTIA
#     distincte (la masse thermique ne change pas à 18h). La chauffe diurne
#     est pilotée par le MÊME alpha + un terme d'apport solaire (sol-air),
#     réduit par les volets et pondéré par l'exposition toiture.
# ═══════════════════════════════════════════════════════════════════════════


# ── Inertie thermique : alpha = exp(-1/τ), τ en heures ─────────────────────
#
#   alpha ↑  ⇔  τ ↑  ⇔  bâtiment lourd, lent à chauffer ET à refroidir.
#   Le type de logement pilote directement τ → impact physique lisible.
#
#   type                τ (h)   commentaire
#   maison_rdc          ~20     maçonnerie + couplage sol, forte masse
#   maison_etage        ~14     masse correcte, moins de couplage sol
#   maison_sous_toits   ~9.5    toiture exposée, masse murs conservée
#   appart_bas          ~25     entouré d'autres logements, très stable
#   appart_haut         ~12     toiture au-dessus, exposition accrue
#   sous_toits          ~6      toiture directe en plafond, faible masse
THERMAL_INERTIA: dict[HousingType, float] = {
    HousingType.climatise:         0.0,   # piloté par thermostat, hors RC
    HousingType.maison_rdc:        0.95,  # τ ≈ 20 h
    HousingType.maison_etage:      0.93,  # τ ≈ 14 h
    HousingType.maison_sous_toits: 0.90,  # τ ≈ 9.5 h
    HousingType.appart_bas:        0.96,  # τ ≈ 25 h
    HousingType.appart_haut:       0.92,  # τ ≈ 12 h
    HousingType.sous_toits:        0.85,  # τ ≈ 6 h
}

# ── Apport solaire diurne (concept de température sol-air) ──────────────────
#
# Uplift de température équivalente au pic solaire (°C), AVANT réduction par
# volets. Représente le rayonnement absorbé par l'enveloppe (surtout toiture)
# qui s'ajoute à la convection de l'air extérieur. Différencié par exposition.
SOLAR_GAIN_PEAK: dict[HousingType, float] = {
    HousingType.climatise:         0.0,
    HousingType.maison_rdc:        2.0,
    HousingType.maison_etage:      2.5,
    HousingType.maison_sous_toits: 4.0,
    HousingType.appart_bas:        2.0,
    HousingType.appart_haut:       4.5,
    HousingType.sous_toits:        6.0,  # toiture directe = source principale
}

# Fraction de l'apport radiatif supprimée par les volets fermés en journée.
# Physiquement : les volets bloquent le rayonnement solaire direct sur les
# vitrages et réduisent l'échauffement des parois exposées.
VOLETS_SOLAR_CUT = 0.70

# Couplage renforcé fenêtres ouvertes : alpha_vent = clip(alpha - 0.30, 0.40, 0.70).
# Plus la masse est lourde (alpha ↑), plus elle réchauffe l'air même volets
# ouverts → cooling nocturne un peu moins efficace par heure. Borné pour rester
# physique : jamais instantané (0.70), jamais négligeable (0.40).
VENT_ALPHA_DROP = 0.30
VENT_ALPHA_MIN  = 0.40
VENT_ALPHA_MAX  = 0.70

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


def _vent_alpha(alpha: float) -> float:
    """Alpha effectif fenêtres ouvertes, borné physiquement."""
    return float(np.clip(alpha - VENT_ALPHA_DROP, VENT_ALPHA_MIN, VENT_ALPHA_MAX))


def _solar_profile(n: int) -> np.ndarray:
    """
    Profil solaire diurne normalisé : cloche 0 → 1 → 0 sur la fenêtre
    (10h–17h), pic au midi solaire. Sert à pondérer SOLAR_GAIN_PEAK.
    """
    if n <= 0:
        return np.array([])
    idx = (np.arange(n) + 0.5) / n
    return np.sin(np.pi * idx)


def _warmup(
    daytime_temps: list[float],
    t_start: float,
    alpha: float,
    solar_peak: float,
    volets_fermes: bool,
) -> float:
    """
    Simule la chauffe diurne (10h–17h) pour estimer T_int à 18h.

    Même physique de masse que la nuit (alpha unifié) + apport solaire :
        T_int(h) = alpha · T_int(h-1) + (1-alpha) · (T_ext(h) + G(h))
    où G(h) = solar_peak · profil(h) · (1 - cut_volets).
    """
    profile = _solar_profile(len(daytime_temps))
    cut     = VOLETS_SOLAR_CUT if volets_fermes else 0.0
    t = t_start
    for t_ext, w in zip(daytime_temps, profile):
        g = solar_peak * float(w) * (1.0 - cut)
        t = alpha * t + (1.0 - alpha) * (t_ext + g)
    return t


def _simulate(
    temps: np.ndarray,
    t_init: float,
    alpha: float,
) -> np.ndarray:
    """
    Modèle RC discret du premier ordre, fenêtres toujours fermées :
        T_int(h) = alpha · T_int(h-1) + (1-alpha) · T_ext(h)
    Le déphasage T_ext → T_int émerge de alpha (τ), sans lag explicite.
    """
    n = len(temps)
    indoor = np.zeros(n)
    indoor[0] = t_init
    for i in range(1, n):
        indoor[i] = alpha * indoor[i - 1] + (1.0 - alpha) * temps[i]
    return indoor


def _simulate_ventilated(
    temps: np.ndarray,
    t_init: float,
    alpha: float,
    alpha_vent: float,
) -> np.ndarray:
    """
    RC + terme de ventilation quand T_int(h-1) - T_ext(h-1) ≥ DELTA_OPEN_MIN.
    Fenêtres ouvertes : couplage renforcé (alpha_vent).
    Fenêtres fermées  : RC nominal (alpha).
    """
    n = len(temps)
    indoor = np.zeros(n)
    indoor[0] = t_init
    for i in range(1, n):
        if indoor[i - 1] - temps[i - 1] >= DELTA_OPEN_MIN:
            indoor[i] = alpha_vent * indoor[i - 1] + (1.0 - alpha_vent) * temps[i]
        else:
            indoor[i] = alpha * indoor[i - 1] + (1.0 - alpha) * temps[i]
    return indoor


# ── Point d'entrée principal ────────────────────────────────────────────────

def compute_indoor_temps(req: ThermalRequest) -> ThermalResponse:
    """
    Calcule les températures intérieures heure par heure (18h → 9h)
    et dérive les recommandations d'ouverture/fermeture de fenêtres.

    Stratégie optimale : fenêtres ouvertes quand T_ext < T_int - delta_min.
    Baseline           : fenêtres toujours fermées (RC pur, sans ventilation).
    """
    alpha      = THERMAL_INERTIA[req.housing]
    alpha_vent = _vent_alpha(alpha)
    solar_peak = SOLAR_GAIN_PEAK[req.housing]
    temps      = np.array(req.hourly_temps, dtype=float)
    rh         = np.array(req.hourly_humidity, dtype=float)
    n          = len(temps)

    if req.debug:
        tau = float("inf") if alpha <= 0 else -1.0 / np.log(alpha)
        print("\n=== DEBUG: Thermal Calculation ===")
        print(f"Housing: {req.housing.value}")
        print(f"Alpha: {alpha}  (τ ≈ {tau:.1f} h)")
        print(f"Alpha ventilé: {round(alpha_vent, 2)}")
        print(f"Solar peak: {solar_peak}°C | Volets fermés: {req.volets_fermes}")
        print(f"indoor_temp_start: {req.indoor_temp_start}°C")
        print(f"Daytime temps (10h-17h): {[round(t, 1) for t in req.daytime_temps]}")

    # ── 1. Warmup diurne (alpha unifié + apport solaire) ───────────────────
    # Cas climatisé : T_int est pilotée par le thermostat, pas de warmup RC.
    if req.housing == HousingType.climatise:
        t_at_18h = req.indoor_temp_start
    else:
        t_at_18h = _warmup(
            req.daytime_temps,
            req.indoor_temp_start,
            alpha,
            solar_peak,
            req.volets_fermes,
        )

    # ── 2. Simulation nocturne — stratégie optimale (avec ventilation) ─────
    indoor = _simulate_ventilated(temps, t_at_18h, alpha, alpha_vent)

    # ── 3. Baseline — fenêtres toujours fermées (RC pur) ───────────────────
    indoor_baseline = _simulate(temps, t_at_18h, alpha)

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

    # ── 4. Construction des données horaires ───────────────────────────────
    hours_data: list[HourlyThermal] = [
        HourlyThermal(
            hour=(START_HOUR + i) % 24,
            outdoor_temp=round(float(temps[i]), 1),
            indoor_temp_estimated=round(float(indoor[i]), 1),
            heat_index=round(_heat_index(float(temps[i]), float(rh[i])), 1),
            delta=round(float(indoor[i] - temps[i]), 1),
            open_window_recommended=bool(indoor[i] - temps[i] >= DELTA_OPEN_MIN),
        )
        for i in range(n)
    ]

    # ── 5. Agrégats ────────────────────────────────────────────────────────
    optimal_open_hour  = next((h.hour for h in hours_data if h.open_window_recommended), None)
    optimal_close_hour = next((h.hour for h in reversed(hours_data) if h.open_window_recommended), None)

    min_idx          = int(np.argmin(temps))
    min_outdoor_hour = (START_HOUR + min_idx) % 24

    # FIX : si le creux absolu tombe après optimal_close_hour dans la nuit,
    # il est hors de la fenêtre d'action — l'utilisateur a déjà fermé.
    # On remonte min_outdoor_hour à optimal_close_hour pour déclencher le guard
    # conflictsClose dans NightPlanEngine et éviter une action incohérente.
    if optimal_close_hour is not None:
        close_idx = next(
            (i for i, h in enumerate(hours_data) if h.hour == optimal_close_hour),
            None,
        )
        if close_idx is not None and min_idx > close_idx:
            min_outdoor_hour = optimal_close_hour  # neutralise le creux post-fermeture

    # Dernière heure fraîche entre 0h et 9h → moment limite pour fermer avant chauffe
    morning_hours      = [h for h in hours_data if 0 <= h.hour <= 9]
    morning_close_hour = next(
        (h.hour for h in reversed(morning_hours) if h.open_window_recommended),
        None,
    )

    min_indoor   = round(float(indoor.min()), 1)
    baseline_min = round(float(indoor_baseline.min()), 1)
    gain         = round(baseline_min - min_indoor, 1)  # positif = stratégie gagnante

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