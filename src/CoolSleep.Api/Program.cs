using CoolSleep.Api.Features.NightPlan;

var builder = WebApplication.CreateBuilder(args);

// ── OpenAPI ───────────────────────────────────────
builder.Services.AddOpenApi();

// ── CORS (Blazor WASM) ────────────────────────────
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// ── Feature : NightPlan ───────────────────────────
builder.Services.AddHttpClient();
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
app.MapGet("/health", async (IHttpClientFactory factory, IConfiguration config) =>
{
    var baseUrl = config["ThermalService:BaseUrl"] ?? "http://localhost:8000";
    var client  = factory.CreateClient();
    try
    {
        var r = await client.GetAsync($"{baseUrl}/health");
        return r.IsSuccessStatusCode
            ? Results.Ok(new { status = "ok", service = "api" })
            : Results.Json(new { status = "degraded", service = "api", thermal = "unhealthy" }, statusCode: 503);
    }
    catch
    {
        return Results.Json(new { status = "degraded", service = "api", thermal = "unreachable" }, statusCode: 503);
    }
})
.WithName("Health");
app.MapNightPlan();

app.Run();

// Requis pour WebApplicationFactory dans les tests d'intégration
public partial class Program { }
