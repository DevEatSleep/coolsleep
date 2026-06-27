using CoolSleep.Api.Features.NightPlan;

var builder = WebApplication.CreateBuilder(args);

// ── OpenAPI ───────────────────────────────────────
builder.Services.AddOpenApi();

// ── CORS (Blazor WASM) ────────────────────────────
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// ── Feature : NightPlan ───────────────────────────
builder.Services.AddHttpClient<OpenMeteoClient>();

builder.Services.AddHttpClient<ThermalClient>(c =>
    c.BaseAddress = new Uri(
        builder.Configuration["ThermalService:BaseUrl"] ?? "http://localhost:8000"));

builder.Services.AddScoped<NightPlanHandler>();

// ── Pipeline ──────────────────────────────────────
var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseCors();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "api" }))
    .WithName("Health");
app.MapNightPlan();

app.Run();

// Requis pour WebApplicationFactory dans les tests d'intégration
public partial class Program { }
