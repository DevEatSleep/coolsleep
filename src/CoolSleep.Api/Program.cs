using CoolSleep.Api.Features.NightPlan;

var builder = WebApplication.CreateBuilder(args);

// ── OpenAPI ───────────────────────────────────────
builder.Services.AddOpenApi();

// ── CORS (Blazor WASM) ────────────────────────────
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// ── Feature : NightPlan ───────────────────────────
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<OpenMeteoClient>(c =>
    c.DefaultRequestHeaders.UserAgent.ParseAdd("CoolSleep/1.0 (+https://coolsleep.onrender.com)"));

builder.Services.AddHttpClient<ThermalClient>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["ThermalService:BaseUrl"] ?? "http://localhost:8000");
    c.Timeout = TimeSpan.FromSeconds(25);
});

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
    string thermalStatus;
    try
    {
        var r = await client.GetAsync($"{baseUrl}/health");
        thermalStatus = r.IsSuccessStatusCode ? "ok" : "unhealthy";
    }
    catch
    {
        thermalStatus = "starting";
    }
    return Results.Ok(new { status = "ok", service = "api", thermal = thermalStatus });
})
.WithName("Health");
app.MapNightPlan();

app.Run();

// Requis pour WebApplicationFactory dans les tests d'intégration
public partial class Program { }
