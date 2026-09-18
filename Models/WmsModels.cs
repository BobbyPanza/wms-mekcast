namespace WMSMekCast.Models;

// ─── Articoli ────────────────────────────────────────────────────────────────

public record ArticleDto(
    string Code,
    string Description,
    string UoM,
    decimal TotalStock,
    List<ArticleLocationDto> Locations,
    List<MovementDto> RecentMovements
);

public record ArticleLocationDto(
    string WarehouseCode,
    string LocationCode,
    decimal Quantity,
    decimal MinQty,
    decimal MaxQty,
    bool IsMainWithdrawal
);

// ─── Locazioni ───────────────────────────────────────────────────────────────

public record LocationDto(
    string WarehouseCode,
    string LocationCode,
    string LocationDesc,
    List<LocationContentDto> Contents,
    List<MovementDto> RecentMovements
);

public record LocationContentDto(
    string ArticleCode,
    string ArticleDesc,
    string UoM,
    decimal Quantity,
    bool IsMainWithdrawal
);

/// <summary>Locazione abbinata a un articolo (riga L_MLPA).</summary>
public class ManagedLocationDto
{
    public string  WarehouseCode { get; set; } = "";
    public string  LocationCode  { get; set; } = "";
    public decimal CurrentQty    { get; set; }
    public bool    IsMain        { get; set; }
}

// ─── Movimenti ───────────────────────────────────────────────────────────────

public record MovementDto(
    DateTime Timestamp,
    string CausalCode,
    string CausalDesc,
    decimal Quantity,
    string OperatorCode,
    string WarehouseCode,
    string LocationCode,
    string ArticleCode = ""
);

public record ErpMovRequest(
    string  ArticleCode,
    string  CausalCode,
    decimal Qty,
    string  WarehouseCode,
    string  LocationCode,
    string  OperatorCode,
    string? ReferenceCode = null
);

// ─── Magazzini ─────────────────────────────────────────────────────────────--

public record WarehouseDto(string Code, string Description, string Type);

/// <summary>Cella/locazione per la mappa magazzino. Occupata = giacenza &gt; 0.</summary>
public record WarehouseCellDto(string LocationCode, bool Occupied, int ItemCount, decimal TotalQty);

// ─── Spostamento multiplo (riga selezionata) ─────────────────────────────────

public class MoveLine
{
    public string  ArticleCode { get; set; } = "";
    public string  ArticleDesc { get; set; } = "";
    public string  UoM         { get; set; } = "";
    public decimal Available   { get; set; }
    public decimal Quantity    { get; set; }
    public bool    Selected    { get; set; }
}

// ─── Stampa (template da appsettings) ─────────────────────────────────────────

public class PrintTemplate
{
    public string Context     { get; set; } = "";   // "ARTICLE", "LOCATION"
    public string Name        { get; set; } = "";
    public string ReportName  { get; set; } = "";    // printModelCode (Printer Manager)
    public string? PrinterName { get; set; }         // override stampante (null = default config)
    public List<PrintTemplateParam> Params { get; set; } = [];
}

public class PrintTemplateParam
{
    public string ParamName   { get; set; } = "";    // nome parametro / JSON key
    public string? AutoFillKey { get; set; }         // chiave auto-fill dal contesto (es. "PACOD","LCCOD")
    public string Label       { get; set; } = "";
    public bool   IsRequired  { get; set; }
}
