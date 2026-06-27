from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from .schemas import ThermalRequest, ThermalResponse
from .model import compute_indoor_temps

app = FastAPI(
    title="CoolSleep Thermal Engine",
    description="Calcul d'inertie thermique et plan de nuit",
    version="0.1.0",
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)


@app.post("/thermal/compute", response_model=ThermalResponse)
def compute(req: ThermalRequest) -> ThermalResponse:
    return compute_indoor_temps(req)


@app.get("/health")
def health() -> dict:
    return {"status": "ok", "service": "thermal"}
