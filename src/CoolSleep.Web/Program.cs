using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using CoolSleep.Web;
using CoolSleep.Web.Services;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"];
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(string.IsNullOrEmpty(apiBaseUrl)
        ? builder.HostEnvironment.BaseAddress
        : apiBaseUrl)
});

builder.Services.AddScoped<NightPlanApiClient>();
builder.Services.AddMudServices();

await builder.Build().RunAsync();
