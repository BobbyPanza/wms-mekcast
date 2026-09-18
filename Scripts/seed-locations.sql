-- ============================================================================
-- Seed locazioni A_LOC — formato XXYZZ
--   XX = 00..10        (11 valori: corridoi)
--   Y  = A..E + T       ( 6 valori: livelli, T = terra)
--   ZZ = 01..25        (25 valori: posizioni)
--   => 11 x 6 x 25 = 1650 locazioni  (es. 00A01, 10T25)
--
-- Idempotente: salta le locazioni già presenti nello stesso magazzino.
-- Valori NOT-NULL come da locazioni reali Mek-Cast: LCSTO = 9, LCDED = 'N'.
-- DB: NOME_SERVER\MEKCAST  ·  Factory
-- ============================================================================
DECLARE @Mg varchar(10) = N'MS';   -- <<< MAGAZZINO destinazione (verifica!)

;WITH
Xs AS (
    SELECT RIGHT('0' + CAST(n AS varchar(2)), 2) AS v
    FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9),(10)) t(n)
),
Ys AS (
    SELECT v FROM (VALUES ('A'),('B'),('C'),('D'),('E'),('T')) t(v)
),
Zs AS (
    SELECT RIGHT('0' + CAST(n AS varchar(2)), 2) AS v
    FROM (SELECT TOP (25) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
          FROM sys.all_objects) t
),
Codes AS (
    SELECT Xs.v + Ys.v + Zs.v AS LCCOD
    FROM Xs CROSS JOIN Ys CROSS JOIN Zs
)
INSERT INTO dbo.A_LOC (MGCOD, LCCOD, LCDSC, LCSTO, LCDED)
SELECT @Mg, c.LCCOD, NULL, 9, 'N'
FROM Codes c
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.A_LOC a
    WHERE a.MGCOD = @Mg AND a.LCCOD = c.LCCOD
);

PRINT CAST(@@ROWCOUNT AS varchar(10)) + ' locazioni create nel magazzino ' + @Mg;
