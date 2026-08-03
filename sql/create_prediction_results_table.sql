-- ============================================================================
-- PredictionResults Tablosu — Tahmin Sonuçları Kalıcı Depolama
-- ============================================================================
-- MD §7 ENTEGRASYON_TAHMIN_SISTEMI.md ile uyumlu.
-- Kişi 4: Veritabanı geri yazımı.
--
-- Tahminler buraya yazılır, model performansı sonradan ölçülebilir.
-- PredictionReady olayına abone olan PredictionDbWriter yazar.
-- ============================================================================

CREATE TABLE [dbo].[PredictionResults] (
    [id]              BIGINT IDENTITY(1,1) PRIMARY KEY,
    [entity_id]       VARCHAR(64)   NOT NULL,
    [ts]              DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    [rul]             FLOAT         NULL,
    [health_score]    FLOAT         NULL,
    [anomaly]         BIT           NULL,
    [fault_mode]      VARCHAR(32)   NULL,
    [source]          VARCHAR(8)     NOT NULL DEFAULT 'rule',  -- 'model' / 'rule'
    [status]          VARCHAR(16)   NOT NULL DEFAULT 'running', -- 'running' / 'standby' / 'fault'
);

-- Performans indeksi: entity_id + zaman bazlı sorgular için
CREATE INDEX [IX_pred_entity_ts] ON [dbo].[PredictionResults] ([entity_id], [ts] DESC);

-- Ek indeks: son N tahmini hızlı çekmek için
CREATE INDEX [IX_pred_ts_desc] ON [dbo].[PredictionResults] ([ts] DESC)
    INCLUDE ([entity_id], [health_score], [anomaly], [source]);

-- ============================================================================
-- Örnek sorgular
-- ============================================================================

-- Son 1 saatteki tüm anomali durumları
-- SELECT entity_id, ts, health_score, anomaly, fault_mode, source
-- FROM PredictionResults
-- WHERE anomaly = 1 AND ts > DATEADD(HOUR, -1, GETUTCDATE())
-- ORDER BY ts DESC;

-- Entity bazlı son sağlık skoru
-- SELECT entity_id, ts, health_score, source, status
-- FROM (
--     SELECT entity_id, ts, health_score, source, status,
--            ROW_NUMBER() OVER (PARTITION BY entity_id ORDER BY ts DESC) AS rn
--     FROM PredictionResults
-- ) sub
-- WHERE rn = 1;

-- Kayıt sayısı (veri kalitesi kontrolü)
-- SELECT COUNT(*) AS total_records,
--        SUM(CASE WHEN source = 'model' THEN 1 ELSE 0 END) AS model_count,
--        SUM(CASE WHEN source = 'rule' THEN 1 ELSE 0 END) AS rule_count
-- FROM PredictionResults;
