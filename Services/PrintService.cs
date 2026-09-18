using System.Drawing.Printing;
using System.Text.Json;
using WMSMekCast.Models;

namespace WMSMekCast.Services;

/// <summary>
/// Stampa etichette via Intesi Printer Manager.
/// I template (articolo / locazione) sono definiti in appsettings → PrintService:Templates.
/// </summary>
public class PrintService
{
    private readonly string? _pmUrl;
    private readonly string? _pmPrinter;
    private readonly string? _pmPath;
    private readonly string  _pmUser;
    private readonly List<PrintTemplate> _templates;
    private readonly HttpClient _http;
    private readonly ILogger<PrintService> _log;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_pmUrl);
    public string? DefaultPrinterName => _pmPrinter;

    public PrintService(IConfiguration config, HttpClient http, ILogger<PrintService> log)
    {
        var s = config.GetSection("PrintService");
        _pmUrl     = s["IntesiPrinterManagerUrl"];
        _pmPrinter = s["IntesiPrinterManagerPrinter"];
        _pmPath    = s["PrinterManagerEndpointPath"];
        _pmUser    = s["UserCode"] ?? "WMS";
        _templates = s.GetSection("Templates").Get<List<PrintTemplate>>() ?? [];
        _http = http;
        _log  = log;
    }

    /// <summary>Template per contesto ("ARTICLE", "LOCATION"). Null se non definito.</summary>
    public PrintTemplate? GetTemplate(string context) =>
        _templates.FirstOrDefault(t => string.Equals(t.Context, context, StringComparison.OrdinalIgnoreCase));

    /// <summary>Stampanti installate sul server (per il dialog).</summary>
    public IReadOnlyList<string> GetInstalledPrinters()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return [];
            var list = new List<string>();
#pragma warning disable CA1416 // chiamata Windows-only — l'app gira su IIS/Windows
            foreach (string p in PrinterSettings.InstalledPrinters) list.Add(p);
#pragma warning restore CA1416
            return list.OrderBy(x => x).ToList();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "GetInstalledPrinters fallito");
            return [];
        }
    }

    public async Task<(bool Ok, string Msg)> PrintAsync(
        PrintTemplate template, string pageName,
        Dictionary<string, string> paramValues, int copies = 1, string? printerOverride = null)
    {
        if (string.IsNullOrWhiteSpace(_pmUrl))
            return (false, "Stampa non configurata (PrintService:IntesiPrinterManagerUrl mancante)");

        try
        {
            var endpoint = _pmUrl.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(_pmPath))
                endpoint += "/" + _pmPath.TrimStart('/');

            var printer = printerOverride ?? template.PrinterName ?? _pmPrinter;

            var payload = new
            {
                opts = new
                {
                    printModelCode = template.ReportName,
                    userCode       = _pmUser,
                    printerName    = printer,
                    description_1  = pageName,
                    description_2  = (string?)null,
                    copyQuantities = copies,
                    jsonParams     = JsonSerializer.Serialize(paramValues)
                }
            };

            var payloadJson = JsonSerializer.Serialize(payload);
            _log.LogInformation("Print → PM {Endpoint}: {Payload}", endpoint, payloadJson);

            var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json")
            };
            req.Headers.Add("x-http-method-override", "PrintReport");
            req.Headers.Add("Accept", "application/json");

            var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode)
                return (true, "Stampa inviata");

            var body = await resp.Content.ReadAsStringAsync();
            _log.LogWarning("PM {Status}: {Body}", (int)resp.StatusCode, body);
            return (false, $"Printer Manager {(int)resp.StatusCode}: {body}");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "PrintAsync fallito per {Report}", template.ReportName);
            return (false, $"Errore: {ex.Message}");
        }
    }
}
