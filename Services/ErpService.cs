using Dapper;
using Microsoft.Data.SqlClient;
using WMSMekCast.Models;

namespace WMSMekCast.Services;

/// <summary>
/// Accesso al DB ERP Intesi (Factory) via Dapper.
/// Tutti i movimenti passano da TRD_InsertMov — mai INSERT diretti su S_MOV.
/// </summary>
public class ErpService
{
    private readonly string? _connStr;
    private readonly string _wh;   // magazzino unico gestito (Wms:DefaultWarehouse, es. MS) — app monoscaffale
    private readonly ILogger<ErpService> _log;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_connStr);

    public ErpService(IConfiguration config, ILogger<ErpService> log)
    {
        _connStr = config.GetConnectionString("ErpDatabase");
        _wh = config["Wms:DefaultWarehouse"] ?? "MS";
        _log = log;
    }

    private SqlConnection Open() => new(_connStr!);

    // ─── Operatori ─────────────────────────────────────────────────────────────

    /// <summary>Valida codice + PIN contro A_OPR. Password vuota = PIN vuoto ammesso.</summary>
    public async Task<(bool Ok, string? Name, string? GroupCode)> TryLoginAsync(string opcod, string pin)
    {
        try
        {
            using var db = Open();
            var row = await db.QueryFirstOrDefaultAsync<OprRow>(
                "SELECT OPCOD, OPDSC, OPPSW, GRCOD FROM dbo.A_OPR WHERE OPCOD = @Opcod",
                new { Opcod = opcod.Trim().ToUpper() });

            if (row is null) return (false, null, null);
            if ((row.OPPSW ?? "").Trim() != (pin ?? "").Trim()) return (false, null, null);

            return (true, row.OPDSC, row.GRCOD ?? "");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "TryLoginAsync fallito per {Opcod}", opcod);
            throw;
        }
    }

    // ─── Magazzini ───────────────────────────────────────────────────────────--

    /// <summary>Magazzini interni (A_MAG.MGTYP = 'I').</summary>
    public async Task<List<WarehouseDto>> GetInternalWarehousesAsync()
    {
        try
        {
            using var db = Open();
            var rows = await db.QueryAsync<MagRow>(
                "SELECT MGCOD, MGDSC, MGTYP FROM dbo.A_MAG WHERE MGTYP = 'I' ORDER BY MGCOD");
            return rows.Select(r => new WarehouseDto(r.MGCOD ?? "", r.MGDSC ?? "", r.MGTYP ?? "")).ToList();
        }
        catch (Exception ex) { _log.LogError(ex, "GetInternalWarehousesAsync"); throw; }
    }

    /// <summary>Tutte le locazioni del magazzino (MS) con stato occupazione, per la mappa. Occupata = giacenza > 0.</summary>
    public async Task<List<WarehouseCellDto>> GetWarehouseMapAsync()
    {
        try
        {
            using var db = Open();
            var rows = await db.QueryAsync<CellRow>(
                @"SELECT a.LCCOD,
                         COUNT(CASE WHEN m.QTLOC > 0 THEN 1 END) AS Items,
                         COALESCE(SUM(CASE WHEN m.QTLOC > 0 THEN m.QTLOC END), 0) AS Qty
                  FROM dbo.A_LOC a
                  LEFT JOIN dbo.L_MLPA m ON m.MGCOD = a.MGCOD AND m.LCCOD = a.LCCOD
                  WHERE a.MGCOD = @Wh
                  GROUP BY a.LCCOD
                  ORDER BY a.LCCOD",
                new { Wh = _wh });
            return rows.Select(r => new WarehouseCellDto(
                (r.LCCOD ?? "").Trim(), r.Items > 0, r.Items, r.Qty)).ToList();
        }
        catch (Exception ex) { _log.LogError(ex, "GetWarehouseMapAsync"); throw; }
    }

    // ─── Articolo ───────────────────────────────────────────────────────────────

    /// <summary>Articolo da A_PAR + giacenze L_MLPA + ultimi movimenti S_MOV. Null se non trovato.</summary>
    public async Task<ArticleDto?> GetArticleAsync(string code)
    {
        try
        {
            using var db = Open();

            var art = await db.QueryFirstOrDefaultAsync<ParRow>(
                "SELECT PACOD, PADSC, PAUDM FROM dbo.A_PAR WHERE PACOD = @Code",
                new { Code = code.Trim() });
            if (art is null) return null;

            var locs = (await db.QueryAsync<MlpaRow>(
                @"SELECT MGCOD, LCCOD, QTLOC, QTMIN, QTMAX, LCPRC
                  FROM dbo.L_MLPA
                  WHERE PACOD = @Code AND MGCOD = @Wh AND QTLOC > 0
                  ORDER BY QTLOC DESC",
                new { Code = art.PACOD, Wh = _wh })).ToList();

            var locationDtos = locs.Select(l => new ArticleLocationDto(
                WarehouseCode:    l.MGCOD ?? "",
                LocationCode:     l.LCCOD ?? "",
                Quantity:         l.QTLOC,
                MinQty:           l.QTMIN ?? 0m,
                MaxQty:           l.QTMAX,
                IsMainWithdrawal: (l.LCPRC ?? "") == "Y"
            )).ToList();

            var movs = await GetArticleMovementsAsync(db, art.PACOD!, _wh);

            return new ArticleDto(
                Code:            art.PACOD ?? code,
                Description:     art.PADSC ?? "",
                UoM:             art.PAUDM ?? "",
                TotalStock:      locationDtos.Sum(l => l.Quantity),
                Locations:       locationDtos,
                RecentMovements: movs
            );
        }
        catch (Exception ex) { _log.LogError(ex, "GetArticleAsync {Code}", code); throw; }
    }

    private static async Task<List<MovementDto>> GetArticleMovementsAsync(SqlConnection db, string pacod, string wh)
    {
        var movs = await db.QueryAsync<MovRow>(
            @"SELECT TOP 30 m.MOSTP, m.CMCOD, c.CMDSC, m.MOQTV, m.OPCOD, m.MGCOD, m.LCCOD
              FROM dbo.S_MOV m
              LEFT JOIN dbo.A_CMM c ON c.CMCOD = m.CMCOD
              WHERE m.PACOD = @Code AND m.MGCOD = @Wh AND m.MOSTP >= DATEADD(month, -6, GETDATE())
              ORDER BY m.MOSTP DESC",
            new { Code = pacod, Wh = wh });
        return movs.Select(m => new MovementDto(
            Timestamp:     m.MOSTP ?? DateTime.MinValue,
            CausalCode:    m.CMCOD ?? "",
            CausalDesc:    m.CMDSC ?? "",
            Quantity:      m.MOQTV ?? 0m,
            OperatorCode:  m.OPCOD ?? "",
            WarehouseCode: m.MGCOD ?? "",
            LocationCode:  m.LCCOD ?? ""
        )).ToList();
    }

    /// <summary>Locazioni abbinate a un articolo (L_MLPA), con giacenza e flag principale.</summary>
    public async Task<List<ManagedLocationDto>> GetArticleLocationsAsync(string articleCode)
    {
        try
        {
            using var db = Open();
            return (await db.QueryAsync<ManagedLocationDto>(
                @"SELECT MGCOD AS WarehouseCode, LCCOD AS LocationCode,
                         QTLOC AS CurrentQty,
                         CASE WHEN LCPRC='Y' THEN 1 ELSE 0 END AS IsMain
                  FROM dbo.L_MLPA
                  WHERE PACOD = @Code AND MGCOD = @Wh
                  ORDER BY LCCOD",
                new { Code = articleCode, Wh = _wh })).ToList();
        }
        catch (Exception ex) { _log.LogError(ex, "GetArticleLocationsAsync {Art}", articleCode); throw; }
    }

    // ─── Locazione ────────────────────────────────────────────────────────────--

    /// <summary>Locazione (A_LOC) + contenuto (L_MLPA) + movimenti. Null se non trovata.</summary>
    public async Task<LocationDto?> GetLocationAsync(string locationCode, string? mgcod = null)
    {
        try
        {
            using var db = Open();

            var loc = await db.QueryFirstOrDefaultAsync<LocRow>(
                @"SELECT TOP 1 MGCOD, LCCOD, LCDSC FROM dbo.A_LOC
                  WHERE LCCOD = @LcCod" + (mgcod is null ? "" : " AND MGCOD = @MgCod"),
                new { LcCod = locationCode.Trim().ToUpper(), MgCod = mgcod });
            if (loc is null) return null;

            var mg = loc.MGCOD ?? "";
            var lc = loc.LCCOD ?? locationCode;

            var contents = (await db.QueryAsync<ContentRow>(
                @"SELECT TOP 300 m.PACOD, p.PADSC, p.PAUDM, m.QTLOC, m.LCPRC
                  FROM dbo.L_MLPA m
                  LEFT JOIN dbo.A_PAR p ON p.PACOD = m.PACOD
                  WHERE m.MGCOD = @MgCod AND m.LCCOD = @LcCod AND m.QTLOC > 0
                  ORDER BY m.QTLOC DESC",
                new { MgCod = mg, LcCod = lc })).ToList();

            var contentDtos = contents.Select(c => new LocationContentDto(
                ArticleCode:      c.PACOD ?? "",
                ArticleDesc:      c.PADSC ?? "",
                UoM:              c.PAUDM ?? "",
                Quantity:         c.QTLOC,
                IsMainWithdrawal: (c.LCPRC ?? "") == "Y"
            )).ToList();

            var movs = (await db.QueryAsync<LocMovRow>(
                @"SELECT TOP 30 m.MOSTP, m.CMCOD, c.CMDSC, m.MOQTV, m.OPCOD, m.PACOD
                  FROM dbo.S_MOV m
                  LEFT JOIN dbo.A_CMM c ON c.CMCOD = m.CMCOD
                  WHERE m.MGCOD = @MgCod AND m.LCCOD = @LcCod
                    AND m.MOSTP >= DATEADD(month, -6, GETDATE())
                  ORDER BY m.MOSTP DESC",
                new { MgCod = mg, LcCod = lc })).ToList();

            var movDtos = movs.Select(m => new MovementDto(
                Timestamp:     m.MOSTP ?? DateTime.MinValue,
                CausalCode:    m.CMCOD ?? "",
                CausalDesc:    m.CMDSC ?? "",
                Quantity:      m.MOQTV ?? 0m,
                OperatorCode:  m.OPCOD ?? "",
                WarehouseCode: mg,
                LocationCode:  lc,
                ArticleCode:   m.PACOD ?? ""
            )).ToList();

            return new LocationDto(mg, lc, loc.LCDSC ?? "", contentDtos, movDtos);
        }
        catch (Exception ex) { _log.LogError(ex, "GetLocationAsync {Code}", locationCode); throw; }
    }

    /// <summary>MGCOD di una locazione da A_LOC. Null se non trovata.</summary>
    public async Task<string?> GetLocationWarehouseAsync(string lccod, string? preferMgcod = null)
    {
        try
        {
            using var db = Open();
            if (preferMgcod is not null)
            {
                var inPref = await db.QueryFirstOrDefaultAsync<string>(
                    "SELECT TOP 1 MGCOD FROM dbo.A_LOC WHERE LCCOD = @Lc AND MGCOD = @Mg",
                    new { Lc = lccod.Trim().ToUpper(), Mg = preferMgcod });
                if (inPref is not null) return inPref;
            }
            return await db.QueryFirstOrDefaultAsync<string>(
                "SELECT TOP 1 MGCOD FROM dbo.A_LOC WHERE LCCOD = @Lc",
                new { Lc = lccod.Trim().ToUpper() });
        }
        catch (Exception ex) { _log.LogError(ex, "GetLocationWarehouseAsync {Lc}", lccod); throw; }
    }

    /// <summary>True se la coppia MGCOD+LCCOD esiste in A_LOC.</summary>
    public async Task<bool> LocationExistsAsync(string mgCod, string lcCod)
    {
        try
        {
            using var db = Open();
            return await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dbo.A_LOC WHERE MGCOD=@Mg AND LCCOD=@Lc",
                new { Mg = mgCod, Lc = lcCod.Trim().ToUpper() }) > 0;
        }
        catch (Exception ex) { _log.LogError(ex, "LocationExistsAsync {Mg}/{Lc}", mgCod, lcCod); throw; }
    }

    /// <summary>
    /// Crea una nuova locazione in A_LOC.
    /// LCSTO (stato) e LCDED (flag eliminata) sono NOT NULL senza default →
    /// valori come da locazioni reali: LCSTO=9, LCDED='N'.
    /// </summary>
    public async Task CreateLocationAsync(string mgCod, string lcCod, string? description)
    {
        try
        {
            using var db = Open();
            await db.ExecuteAsync(
                @"INSERT INTO dbo.A_LOC (MGCOD, LCCOD, LCDSC, LCSTO, LCDED)
                  VALUES (@Mg, @Lc, @Dsc, 9, 'N')",
                new { Mg = mgCod, Lc = lcCod.Trim().ToUpper(), Dsc = description });
        }
        catch (Exception ex) { _log.LogError(ex, "CreateLocationAsync {Mg}/{Lc}", mgCod, lcCod); throw; }
    }

    /// <summary>Disabbina (rimuove) la riga L_MLPA articolo↔locazione solo se QTLOC = 0.</summary>
    public async Task<(bool Ok, string Msg)> UnassignLocationAsync(string articleCode, string mgCod, string lcCod)
    {
        try
        {
            using var db = Open();
            var qty = await db.ExecuteScalarAsync<decimal?>(
                "SELECT QTLOC FROM dbo.L_MLPA WHERE PACOD=@Pa AND MGCOD=@Mg AND LCCOD=@Lc",
                new { Pa = articleCode, Mg = mgCod, Lc = lcCod });
            if (qty is null) return (false, "Abbinamento non trovato");
            if (qty != 0m)   return (false, $"Giacenza presente ({qty:0.###}): svuota la locazione prima di disabbinare");

            await db.ExecuteAsync(
                "DELETE FROM dbo.L_MLPA WHERE PACOD=@Pa AND MGCOD=@Mg AND LCCOD=@Lc",
                new { Pa = articleCode, Mg = mgCod, Lc = lcCod });
            return (true, $"Disabbinata {mgCod}/{lcCod} da {articleCode}");
        }
        catch (Exception ex) { _log.LogError(ex, "UnassignLocationAsync {Art}", articleCode); throw; }
    }

    // ─── Movimenti ────────────────────────────────────────────────────────────--

    /// <summary>Movimento singolo (carico/scarico) via TRD_InsertMov. Ritorna (ok, msg, idMov).</summary>
    public async Task<(bool Ok, string Msg, int IdMov)> InsertMovAsync(ErpMovRequest req)
    {
        try
        {
            using var db = Open();
            var p = BuildMovParams(req.ArticleCode, req.CausalCode, req.Qty,
                                   req.WarehouseCode, req.LocationCode,
                                   req.OperatorCode, req.ReferenceCode ?? "");
            await db.ExecuteAsync("dbo.TRD_InsertMov", p, commandType: System.Data.CommandType.StoredProcedure);
            var id = p.Get<int>("@ReturnVal");
            return id > 0
                ? (true, $"{req.CausalCode} #{id} registrato", id)
                : (false, $"Movimento {req.CausalCode} fallito (IDMOV={id} — causale valida?)", id);
        }
        catch (Exception ex) { _log.LogError(ex, "InsertMovAsync {Art}/{Cm}", req.ArticleCode, req.CausalCode); throw; }
    }

    /// <summary>Spostamento articolo: SMI su origine + CMI su destinazione, stesso referenceCode.</summary>
    public async Task<(bool Ok, string Msg)> ExecuteMoveAsync(
        string pacod, string srcMgcod, string srcLccod,
        string dstMgcod, string dstLccod, decimal qty,
        string operatorCode, string outCausal, string inCausal, string refCode)
    {
        try
        {
            using var db = Open();

            var pOut = BuildMovParams(pacod, outCausal, qty, srcMgcod, srcLccod, operatorCode, refCode);
            await db.ExecuteAsync("dbo.TRD_InsertMov", pOut, commandType: System.Data.CommandType.StoredProcedure);
            var idOut = pOut.Get<int>("@ReturnVal");
            if (idOut <= 0) return (false, $"{pacod}: scarico origine fallito (causale {outCausal}?)");

            var pIn = BuildMovParams(pacod, inCausal, qty, dstMgcod, dstLccod, operatorCode, refCode);
            await db.ExecuteAsync("dbo.TRD_InsertMov", pIn, commandType: System.Data.CommandType.StoredProcedure);
            var idIn = pIn.Get<int>("@ReturnVal");
            if (idIn <= 0) return (false, $"{pacod}: carico destinazione fallito (causale {inCausal}?)");

            return (true, $"{pacod}: {outCausal} #{idOut} / {inCausal} #{idIn}");
        }
        catch (Exception ex) { _log.LogError(ex, "ExecuteMoveAsync {Art}", pacod); throw; }
    }

    private static DynamicParameters BuildMovParams(
        string pacod, string cmcod, decimal qty,
        string mgcod, string lccod, string operatorCode, string refCode)
    {
        var p = new DynamicParameters();
        p.Add("@sPACOD",        pacod);
        p.Add("@sCMCOD",        cmcod);
        p.Add("@fMOQTV",        qty);
        p.Add("@sMGCOD",        mgcod);
        p.Add("@sLCCOD",        lccod);
        p.Add("@operatorCode",  operatorCode);
        p.Add("@referenceCode", refCode);
        p.Add("@ReturnVal",     dbType: System.Data.DbType.Int32,
              direction: System.Data.ParameterDirection.ReturnValue);
        return p;
    }

    // ─── Row DTO (mapping Dapper) ─────────────────────────────────────────────--

    private class OprRow { public string? OPCOD { get; set; } public string? OPDSC { get; set; } public string? OPPSW { get; set; } public string? GRCOD { get; set; } }
    private class MagRow { public string? MGCOD { get; set; } public string? MGDSC { get; set; } public string? MGTYP { get; set; } }
    private class ParRow { public string? PACOD { get; set; } public string? PADSC { get; set; } public string? PAUDM { get; set; } }
    private class CellRow { public string? LCCOD { get; set; } public int Items { get; set; } public decimal Qty { get; set; } }
    private class MlpaRow { public string? MGCOD { get; set; } public string? LCCOD { get; set; } public decimal QTLOC { get; set; } public decimal? QTMIN { get; set; } public decimal QTMAX { get; set; } public string? LCPRC { get; set; } }
    private class MovRow { public DateTime? MOSTP { get; set; } public string? CMCOD { get; set; } public string? CMDSC { get; set; } public decimal? MOQTV { get; set; } public string? OPCOD { get; set; } public string? MGCOD { get; set; } public string? LCCOD { get; set; } }
    private class LocRow { public string? MGCOD { get; set; } public string? LCCOD { get; set; } public string? LCDSC { get; set; } }
    private class ContentRow { public string? PACOD { get; set; } public string? PADSC { get; set; } public string? PAUDM { get; set; } public decimal QTLOC { get; set; } public string? LCPRC { get; set; } }
    private class LocMovRow { public DateTime? MOSTP { get; set; } public string? CMCOD { get; set; } public string? CMDSC { get; set; } public decimal? MOQTV { get; set; } public string? OPCOD { get; set; } public string? PACOD { get; set; } }
}
