# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this project does

CoolSleep generates a personalized night cooling plan during heatwaves. Given a city, GPS coordinates, and housing type, it computes when to open/close windows to minimize indoor temperature overnight.

## Architecture

Three-tier system:

```
Browser (Blazor WASM PWA)
  ├── GET api.open-meteo.com (free, no key) — fetched client-side from the
  │     USER's IP to avoid the shared-host 429 rate-limit
  └── POST /api/nightplan  { city, housing, hourlyTemps[], hourlyHumidity[], sunrise[] }
        └── CoolSleep.Api (ASP.NET Core 10, port 5000)
              ├── NightPlanHandler slices hours 18h→09h next day (16 data points)
              └── ThermalClient → POST /thermal/compute
                    └── Python FastAPI (port 8000)
                          └── NumPy thermal inertia model
              └── NightPlanEngine.Build() → risk score + action list
```

**Why weather is fetched client-side**: Open-Meteo rate-limits per IP. On a shared
host (Render free tier) the server's egress IP gets 429'd by other tenants, so the
Blazor app fetches the forecast directly (per-user IP quota) and forwards the raw
hourly arrays to the API.

**C# API** (`src/CoolSleep.Api`) follows vertical slice architecture:
- `Core/` — domain enums, `HeatIndexCalculator` (Steadman/NOAA), `NightPlanEngine`
- `Features/NightPlan/` — endpoint, handler, `ThermalClient`, request/response records

**Python service** (`python/thermal/`) is a pure computation microservice:
- `schemas.py` — Pydantic models and `HousingType` enum (snake_case strings)
- `model.py` — thermal inertia: `T_int(h) = α·T_int(h-1) + (1-α)·T_ext(h-lag)`, constants keyed by `HousingType`
- `main.py` — two routes: `POST /thermal/compute` and `GET /health`

**Blazor WASM** (`src/CoolSleep.Web`) is frontend only — `NightPlanApiClient` calls the C# API.

## HousingType sync

`HousingType` exists in both C# (`Core/HousingType.cs`, PascalCase) and Python (`thermal/schemas.py`, snake_case). `ThermalClient.ToSnakeCase()` converts between them. When adding a new housing type, update all four locations: both enum files + `ThermalClient.ToSnakeCase()` + `model.py` constants (`THERMAL_INERTIA`, `LAG_HOURS`).

## Commands

### Python thermal service

```bash
cd python
.\coolsleep_venv\Scripts\Activate.ps1   # Windows
uvicorn thermal.main:app --reload --port 8000
# Swagger: http://localhost:8000/docs
```

```bash
pytest tests/ -v          # run all Python tests
pytest tests/ -v -k "sous_toits"   # run a single test by name
```

### .NET API (requires Python service on port 8000)

```bash
dotnet run --project src/CoolSleep.Api
dotnet test                            # all tests
dotnet test --filter "FullyQualifiedName~NightPlanEngine"  # single class
```

### Blazor frontend

```bash
dotnet run --project src/CoolSleep.Web
```

## Testing approach

C# tests use concrete subclasses (not mocks) — `OpenMeteoClient` and `ThermalClient` have `virtual` methods that file-scoped fake classes override directly in the test file. Do not introduce `NSubstitute` mocks or interfaces for these clients.

## Key config

| Key | Default | Where |
|---|---|---|
| `ThermalService:BaseUrl` | `http://localhost:8000` | `src/CoolSleep.Api/appsettings.json` |
| `ApiBaseUrl` | `http://localhost:5000/` | Blazor `wwwroot/appsettings.json` |

## Heat index formula

The Steadman/NOAA formula is intentionally duplicated in both `Core/HeatIndexCalculator.cs` and `python/thermal/model.py`. C# uses it for risk scoring; Python uses it to annotate hourly output. Keep both in sync if coefficients change.

## Workflow preferences

Do not automatically run tests or start services for every change. Edit files, make the changes, and only run/test on explicit request. Do not commit automatically — wait for user instruction.

## Design principles

### Guidelines for new contributions

- **SRP** (Single Responsibility): Each method has one reason to change. `BuildActions` in `NightPlanEngine.cs` is a known exception (orchestrates 7 related decisions) — do not worsen it by adding unrelated logic. Extract cohesive sub-concerns into separate methods.
- **YAGNI** (You Aren't Gonna Need It): Do not add parameters, enum members, or return fields that are never wired end-to-end. Every parameter must have a real call site; every response field must be consumed. Speculative API surface creates maintenance burden.
- **DRY** (Don't Repeat Yourself): Do not re-derive a value that an upstream service already computed. Example: `NightPlanHandler` re-computes `Min(OutdoorTemp)` and `Average(Humidity)` that the Python thermal service already knows. Request these from the service instead.
- **KISS** (Keep It Simple): Avoid premature abstraction. Use concrete classes with `virtual` methods for testing (as with `OpenMeteoClient`, `ThermalClient`); do not introduce interfaces without proven need. Avoid magic numbers (e.g., `raw[^5..]` slice on sunrise parsing).
