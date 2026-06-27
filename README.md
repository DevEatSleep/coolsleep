# CoolSleep

**Stay cool during heatwaves.** CoolSleep generates personalized night cooling plans to help you manage indoor temperature overnight by strategically opening and closing windows and shutters.

Enter your location and housing type, and CoolSleep tells you when to open/close windows and draw shutters to minimize indoor temperature during the hottest nights.

---

## For Users

### What CoolSleep Does

During a heatwave, outdoor temperatures can stay dangerously high well into the night. CoolSleep uses:

- **Real-time weather data** from Open-Meteo (free API)
- **Thermal modeling** accounting for building type and thermal inertia
- **Heat index calculations** (Steadman/NOAA formula)

...to compute a personalized action plan for your specific location and housing type.

### How to Use

1. Open CoolSleep at [https://coolsleep.netlify.app](https://coolsleep.netlify.app)
2. Enter your city or GPS coordinates
3. Select your housing type (apartment, house, etc.)
4. Get a personalized cooling plan with:

   - **Risk level** (Low / Medium / High)
   - **Hourly actions** (close shutters, open windows)
   - **Optimal window hours** to maximize natural cooling
   - **Predicted minimum indoor temperature**
   - **Morning temperature input** to refine the simulation

### Example

For Paris during a heatwave with morning temperature of 24°C:

- **18:00** — Close shutters (south-facing)
- **22:00** — Open windows fully (coolest outdoor air)
- **04:00** — Close windows (temperature rising)
- **06:00** — Reopen windows to store coolness before heat

---

## For Developers

### Architecture

Three-tier system built on a vertical slice pattern:

```
Browser (Blazor WASM PWA)
  └── GET /api/nightplan?city=&lat=&lon=&housing=
        └── ASP.NET Core 10 API (port 5000)
              ├── OpenMeteoClient → Weather data (18h→09h, 16 points)
              ├── ThermalClient → POST to Python service
              └── NightPlanEngine → Risk scoring + action list
                    └── Python FastAPI (port 8000)
                          └── NumPy thermal inertia model
```

**C# API** (`src/CoolSleep.Api`) — Vertical slice architecture:

- `Core/` — Domain enums, `HeatIndexCalculator`, `NightPlanEngine`
- `Features/NightPlan/` — HTTP endpoint, handler, both clients, request/response records

**Python service** (`python/thermal/`) — Pure computation microservice:

- `schemas.py` — Pydantic models, `HousingType` enum
- `model.py` — Thermal inertia formula + housing constants
- `api.py` — Two routes: `POST /thermal/compute` and `GET /health`

**Blazor WASM** (`src/CoolSleep.Web`) — Frontend only:

- User input form, result display
- `NightPlanApiClient` calls the C# API

### Project Structure

```
src/
├── CoolSleep.Api/
│   ├── Core/                    ← Domain logic
│   │   ├── HeatIndexCalculator.cs
│   │   ├── NightPlanEngine.cs
│   │   └── HousingType.cs
│   ├── Features/NightPlan/      ← Vertical slice
│   │   ├── Endpoint.cs
│   │   ├── Handler.cs
│   │   ├── OpenMeteoClient.cs
│   │   ├── ThermalClient.cs
│   │   └── Request/Response
│   └── Program.cs
└── CoolSleep.Web/               ← Blazor WASM PWA
    ├── Pages/
    ├── Models/
    └── Services/

python/thermal/
├── api.py                       ← FastAPI app
├── model.py                     ← Thermal inertia
├── schemas.py                   ← Pydantic models
└── tests/

tests/CoolSleep.Tests/
├── Core/
└── Features/NightPlan/
```

### Getting Started

#### Prerequisites

- .NET 10 SDK
- Python 3.9+ with `pip`
- VS Code or Visual Studio

#### 1. Python Thermal Service

```bash
cd python

# Windows
.\coolsleep_venv\Scripts\Activate.ps1
# macOS/Linux
source .venv/bin/activate

# Install dependencies (first time only)
pip install -r requirements.txt

# Start the service
uvicorn thermal.api:app --reload --port 8000
```

The service will be available at `http://localhost:8000/docs` (Swagger UI).

#### 2. ASP.NET Core API

```bash
# Terminal 2
dotnet run --project src/CoolSleep.Api
```

API available at `http://localhost:5000/swagger` (Swagger UI).

#### 3. Blazor Frontend

```bash
# Terminal 3
dotnet run --project src/CoolSleep.Web
```

Web app available at `https://localhost:7000`.

### Testing

```bash
# .NET (all tests)
dotnet test

# .NET (single class)
dotnet test --filter "FullyQualifiedName~NightPlanEngine"

# Python (all tests)
cd python && pytest tests/ -v

# Python (single test by name)
pytest tests/ -v -k "sous_toits"
```

### Key Configuration

| Setting             | Default                      | Location                                      |
|---------------------|------------------------------|-----------------------------------------------|
| Python service URL  | `http://localhost:8000`      | `src/CoolSleep.Api/appsettings.json`          |
| API base URL        | `http://localhost:5000/`     | `src/CoolSleep.Web/wwwroot/appsettings.json`  |

### API Endpoint

```
GET /api/nightplan?city=Paris&lat=48.8566&lon=2.3522&housing=AppartHaut
```

**Response:**

```json
{
  "city": "Paris",
  "riskLevel": "Eleve",
  "riskScore": 72,
  "minIndoorTemp": 21.5,
  "optimalOpenHour": 22,
  "optimalCloseHour": 4,
  "actions": [
    { "hour": 18, "label": "Close south-facing shutters", "actionType": "FermerVolets" },
    { "hour": 22, "label": "Open windows fully", "actionType": "OuvrirFenetres" },
    { "hour": 4,  "label": "Close windows", "actionType": "FermerFenetres" },
    { "hour": 6,  "label": "Reopen — store coolness", "actionType": "OuvrirMatin" }
  ]
}
```

### Important Notes for Contributors

- **HousingType sync**: This enum exists in both C# (`Core/HousingType.cs`, PascalCase) and Python (`thermal/schemas.py`, snake_case). When adding a new type, update:

  - `src/CoolSleep.Api/Core/HousingType.cs`
  - `python/thermal/schemas.py`
  - `HousingTypeExtensions.ToSnakeCase()` method
  - Housing constants in `python/thermal/model.py` (`THERMAL_INERTIA`, `LAG_HOURS`, `VOLETS_ALPHA_BONUS`)

- **Heat Index Formula**: Duplicated in both `Core/HeatIndexCalculator.cs` (C#) and `python/thermal/model.py`. Keep in sync if coefficients change.

- **Testing approach**: C# tests use concrete subclasses, not mocks. `OpenMeteoClient` and `ThermalClient` have `virtual` methods that file-scoped fakes override in test files. Do not introduce `NSubstitute` mocks or interfaces.

---

## Deployment

| Component          | Host              | Cost |
|--------------------|-------------------|------|
| Blazor WASM        | Netlify           | Free |
| ASP.NET Core API   | Koyeb (Frankfurt) | Free |
| FastAPI Python     | Koyeb (Frankfurt) | Free |
