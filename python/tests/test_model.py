from thermal.schemas import ThermalRequest, HousingType
from thermal.model import compute_indoor_temps

TEMPS    = [33, 31, 29, 27, 25, 24, 23, 22, 21, 21, 20, 19, 19, 18, 18, 19]
HUMIDITY = [35, 38, 42, 46, 50, 53, 55, 57, 58, 59, 60, 61, 61, 62, 62, 60]
DAYTIME  = [28, 30, 33, 35, 37, 38, 37, 35]  # T_ext 10h–17h


def req(housing: HousingType) -> ThermalRequest:
    return ThermalRequest(
        hourly_temps=TEMPS,
        hourly_humidity=HUMIDITY,
        daytime_temps=DAYTIME,
        housing=housing,
        indoor_temp_start=29.0,
    )


def test_sous_toits_retient_plus_chaleur_que_maison():
    r_toits  = compute_indoor_temps(req(HousingType.sous_toits))
    r_maison = compute_indoor_temps(req(HousingType.maison_rdc))
    assert r_toits.min_indoor_reachable > r_maison.min_indoor_reachable


def test_heure_ouverture_existe_pour_nuit_chaude():
    result = compute_indoor_temps(req(HousingType.appart_haut))
    assert result.optimal_open_hour is not None


def test_nombre_heures_correspond_entree():
    result = compute_indoor_temps(req(HousingType.appart_bas))
    assert len(result.hours) == len(TEMPS)


def test_min_indoor_inferieur_a_text_dehors_debut():
    # Après warmup, T_int doit descendre sous T_ext(18h) au fil de la nuit fraîche
    result = compute_indoor_temps(req(HousingType.maison_rdc))
    assert result.min_indoor_reachable < TEMPS[0]


def test_climatise_pas_d_inertie():
    result = compute_indoor_temps(req(HousingType.climatise))
    # Sans inertie, T_int suit T_ext immédiatement
    assert result.min_indoor_reachable <= req(HousingType.appart_bas).indoor_temp_start


def test_min_outdoor_hour_est_dans_la_nuit():
    # TEMPS décroît de 18h jusqu'au creux puis remonte → le min est quelque part en milieu/fin de nuit
    result = compute_indoor_temps(req(HousingType.appart_bas))
    valid_range = list(range(20, 24)) + list(range(0, 9))
    assert result.min_outdoor_hour in valid_range


def test_indoor_18h_realiste_en_canicule():
    # En canicule (T_ext=40°C toute la journée), après warmup indoor[0] doit être proche de T_ext
    temps_canicule = [40] * 16
    result = compute_indoor_temps(ThermalRequest(
        hourly_temps=temps_canicule,
        hourly_humidity=[40] * 16,
        daytime_temps=[40] * 8,
        housing=HousingType.appart_bas,
        indoor_temp_start=24.0,
    ))
    assert result.hours[0].indoor_temp_estimated > 35.0


def test_morning_close_hour_est_None_si_matin_chaud():
    # Températures élevées toute la nuit et le matin → aucune heure fraîche le matin
    temps_chauds = [35] * 16
    humidity_std = [50] * 16
    r = compute_indoor_temps(ThermalRequest(
        hourly_temps=temps_chauds,
        hourly_humidity=humidity_std,
        daytime_temps=[38] * 8,
        housing=HousingType.appart_bas,
        indoor_temp_start=28.0,
    ))
    assert r.morning_close_hour is None


def test_volets_ouverts_indoor_plus_chaud():
    # Profil diurne monotone croissant : T_ext monte de 10h à 17h sans retomber.
    # Dans ce cas, un alpha plus faible (volets ouverts) suit mieux T_ext montante →
    # T_int(18h) plus élevée qu'avec l'alpha renforcé des volets fermés.
    daytime_croissant = [30, 32, 34, 36, 37, 38, 39, 40]
    base = ThermalRequest(
        hourly_temps=TEMPS,
        hourly_humidity=HUMIDITY,
        daytime_temps=daytime_croissant,
        housing=HousingType.appart_bas,
        indoor_temp_start=24.0,
    )
    r_fermes  = compute_indoor_temps(base.model_copy(update={"volets_fermes": True}))
    r_ouverts = compute_indoor_temps(base.model_copy(update={"volets_fermes": False}))
    assert r_ouverts.hours[0].indoor_temp_estimated >= r_fermes.hours[0].indoor_temp_estimated
