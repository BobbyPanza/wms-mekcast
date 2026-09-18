-- ============================================================================
-- Diagnostica spostamento: perché 01A01 non mostra HF10020029-03 Rev.1
-- Esegui i 3 blocchi e incolla i risultati.
-- ============================================================================

-- 1) Dov'è davvero l'articolo? (in quale magazzino/locazione e con che giacenza)
SELECT MGCOD, '[' + LCCOD + ']' AS LCCOD_bracket, PACOD, QTLOC, LCPRC
FROM dbo.L_MLPA
WHERE PACOD LIKE 'HF10020029-03%'
ORDER BY MGCOD, LCCOD;

-- 2) Cosa contiene la locazione 01A01 in tutti i magazzini?
SELECT MGCOD, '[' + LCCOD + ']' AS LCCOD_bracket, PACOD, QTLOC
FROM dbo.L_MLPA
WHERE LCCOD LIKE '01A01%'
ORDER BY MGCOD, PACOD;

-- 3) In quali magazzini esiste 01A01 in anagrafica A_LOC?
SELECT MGCOD, '[' + LCCOD + ']' AS LCCOD_bracket, LCDSC
FROM dbo.A_LOC
WHERE LCCOD LIKE '01A01%'
ORDER BY MGCOD;
