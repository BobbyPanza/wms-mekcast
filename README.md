# WMS Mek-Cast

Mini-gestionale di magazzino web (PC + cellulare) per il cliente **Mek-Cast**,
modellato su `C:\dev\WMS` ma ridotto alle transazioni essenziali.

Stack: **Blazor Web App / .NET 10 · MudBlazor v9 · SQL Server (Dapper)**
Hosting: **IIS** (terminazione HTTPS esterna, nessun `UseHttpsRedirection`).

## Funzioni

| Tile | Route | Cosa fa |
|------|-------|---------|
| Interroga Articolo | `/article` | A_PAR + giacenze L_MLPA + ultimi movimenti. Bottone stampa **etichetta articolo**. |
| Interroga Locazione | `/location` | A_LOC + articoli presenti + ultimi movimenti. Bottone stampa **etichetta locazione**. |
| Carico | `/load` | Movimento singolo **CAR** (+) su articolo/locazione via `TRD_InsertMov`. |
| Scarico | `/unload` | Movimento singolo **SCAR** (−) via `TRD_InsertMov`. |
| Sposta Articoli | `/move` | Da una locazione origine seleziona **uno o più** articoli → locazione destinazione (`SMI`+`CMI` per articolo). |
| Crea Locazione | `/locations/new` | INSERT in `A_LOC` nel magazzino corrente. |
| Disabbina Locazione | `/unassign` | Rimuove la riga `L_MLPA` articolo↔locazione (solo se giacenza = 0). |

Login operatore contro `A_OPR` (codice + PIN = `OPPSW`).
Selettore **magazzino corrente** in alto a destra (default **MS**, persistito in `localStorage`);
elenco da `A_MAG` (magazzini interni, `MGTYP='I'`: MS, 001, MAG-A).

## Database

Istanza: `NOME_SERVER\MEKCAST` — DB **Factory** (ERP Intesi).
Connection string in `appsettings.json` → `ConnectionStrings:ErpDatabase`.

- **Mai** INSERT diretti su `S_MOV` → sempre `dbo.TRD_InsertMov`.
- Causali (`A_CMM`): `CAR` (+1) carico, `SCAR` (−1) scarico, `SMI`/`CMI` spostamento interno, `INV+`/`INV-` rettifiche. Configurabili in `appsettings.json → Wms`.
- `A_LOC.LCSTO` è NOT NULL senza default → la creazione locazione lo valorizza a 0.

## Stampa

Etichette via **Intesi Printer Manager**. I template stanno in `appsettings.json → PrintService:Templates`
(contesti `ARTICLE` e `LOCATION`, con `ReportName` = printModelCode e parametri auto-compilati).
Finché `IntesiPrinterManagerUrl` è vuoto la stampa è disabilitata ma i valori vengono comunque compilati nel dialog.

## Avvio in sviluppo

```bash
dotnet run --urls http://localhost:5180
```

## Publish su IIS

```bash
dotnet publish -c Release -o C:\intesi\WS\WMSMekCast
```

Preferire un **sito root dedicato** — un sub-app `localhost/wms` rompe SignalR.
