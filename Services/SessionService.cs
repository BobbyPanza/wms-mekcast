namespace WMSMekCast.Services;

/// <summary>
/// Stato per circuito SignalR: operatore loggato, magazzino corrente, contesto stampa.
/// Scoped — non registrare come singleton.
/// </summary>
public class SessionService
{
    public string? OperatorCode { get; private set; }
    public string? OperatorName { get; private set; }
    public string? GroupCode    { get; private set; }
    public bool IsLoggedIn => OperatorCode is not null;

    /// <summary>Magazzino su cui si opera (default da config, modificabile in AppBar).</summary>
    public string CurrentWarehouse { get; private set; } = "MS";

    public string PageTitle { get; private set; } = "WMS Mek-Cast";

    // Contesto stampa — impostato dalla pagina corrente
    public string PrintContext { get; private set; } = "";
    public Dictionary<string, string> PrintAutoFill { get; private set; } = new();

    public event Action? OnChange;

    public void Login(string code, string name, string groupCode)
    {
        OperatorCode = code;
        OperatorName = name;
        GroupCode    = groupCode;
        NotifyStateChanged();
    }

    public void Logout()
    {
        OperatorCode = null;
        OperatorName = null;
        GroupCode    = null;
        NotifyStateChanged();
    }

    public void SetWarehouse(string mgcod)
    {
        CurrentWarehouse = mgcod;
        NotifyStateChanged();
    }

    public void SetPageTitle(string title)
    {
        PageTitle = title;
        NotifyStateChanged();
    }

    /// <summary>
    /// Imposta il contesto stampa per la pagina corrente.
    /// autoFill: valori pre-compilati nei parametri del template (es. {"PACOD","ART001"}).
    /// </summary>
    public void SetPrintContext(string context, Dictionary<string, string>? autoFill = null)
    {
        PrintContext  = context;
        PrintAutoFill = autoFill ?? new();
        NotifyStateChanged();
    }

    public void ClearPrintContext()
    {
        PrintContext  = "";
        PrintAutoFill = new();
        NotifyStateChanged();
    }

    public void NotifyStateChanged() => OnChange?.Invoke();
}
