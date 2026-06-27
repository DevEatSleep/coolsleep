from pydantic import BaseModel, model_validator
from enum import Enum


class HousingType(str, Enum):
    climatise          = "climatise"
    maison_rdc         = "maison_rdc"
    maison_etage       = "maison_etage"
    maison_sous_toits  = "maison_sous_toits"
    appart_bas         = "appart_bas"
    appart_haut        = "appart_haut"
    sous_toits         = "sous_toits"


class ThermalRequest(BaseModel):
    hourly_temps:       list[float]   # T_ext 18h → 9h (15 valeurs nominales)
    hourly_humidity:    list[float]   # RH  18h → 9h (même longueur)
    daytime_temps:      list[float]   # T_ext 10h → 17h (8 valeurs pour warmup)
    housing:            HousingType
    indoor_temp_start:  float = 24.0  # T_int mesurée le matin (avant la chauffe)
    volets_fermes:      bool  = True  # volets fermés pendant la journée

    @model_validator(mode="after")
    def check_lengths(self) -> "ThermalRequest":
        if len(self.hourly_temps) != len(self.hourly_humidity):
            raise ValueError(
                f"hourly_temps ({len(self.hourly_temps)}) et "
                f"hourly_humidity ({len(self.hourly_humidity)}) doivent avoir la même longueur"
            )
        if len(self.hourly_temps) < 2:
            raise ValueError("Minimum 2 heures de données requises")
        if len(self.daytime_temps) == 0:
            raise ValueError("daytime_temps ne peut pas être vide")
        return self


class HourlyThermal(BaseModel):
    hour:                    int
    outdoor_temp:            float
    indoor_temp_estimated:   float
    heat_index:              float
    delta:                   float   # T_int - T_ext (>0 = ouvrir bénéfique)
    open_window_recommended: bool


class ThermalResponse(BaseModel):
    hours:                list[HourlyThermal]
    optimal_open_hour:    int | None   # première heure où ouvrir est pertinent
    optimal_close_hour:   int | None   # dernière heure où ouvrir est pertinent
    morning_close_hour:   int | None   # dernière heure fraîche 0h–9h (fermer après)
    min_indoor_reachable: float        # T_int min atteignable avec stratégie optimale
    baseline_min_indoor:  float        # T_int min si fenêtres toujours fermées
    min_outdoor_hour:     int          # heure du creux de T_ext
    gain_vs_baseline:     float        # écart T_int (baseline - optimal), positif = gain
