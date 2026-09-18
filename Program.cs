using System.Globalization;
using Microsoft.AspNetCore.HttpOverrides;
using MudBlazor.Services;
using WMSMekCast.Components;
using WMSMekCast.Services;

CultureInfo.DefaultThreadCurrentCulture   = new CultureInfo("it-IT");
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("it-IT");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => options.DetailedErrors = true);

builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.BottomCenter;
    config.SnackbarConfiguration.ShowTransitionDuration = 200;
    config.SnackbarConfiguration.HideTransitionDuration = 200;
    config.SnackbarConfiguration.VisibleStateDuration   = 2500;
});

// Scoped = per circuito SignalR (= per sessione utente in Blazor Server)
builder.Services.AddScoped<SessionService>();

// Singleton = accesso al DB ERP reale (connessioni aperte/chiuse per chiamata)
builder.Services.AddSingleton<ErpService>();

// Singleton = servizio stampa (Intesi Printer Manager), template da appsettings
builder.Services.AddHttpClient<PrintService>();

// IIS / reverse proxy support
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// NOTA: niente UseHttpsRedirection — la terminazione HTTPS è gestita da IIS
app.UseAntiforgery();
app.UseStaticFiles();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
