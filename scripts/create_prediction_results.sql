-- PredictionResults tablosu — §7 ENTEGRASYON_TAHMIN_SISTEMI.md
-- Kişi 4: Kalıcı tahmin geçmişi, model performansı sonradan ölçülebilir

CREATE TABLE PredictionResults (
    id            BIGINT IDENTITY PRIMARY KEY,
    entity_id     VARCHAR(64)   NOT NULL,
    ts            DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    rul           FLOAT,
    health_score  FLOAT,
    anomaly       BIT           NOT NULL DEFAULT 0,
    fault_mode    VARCHAR(32),
    source        VARCHAR(8)    NOT NULL DEFAULT 'rule'  -- 'model' / 'rule'
);

CREATE INDEX IX_pred_entity_ts ON PredictionResults(entity_id, ts DESC);

-- Örnek sorgu: son 1 saatteki anomali oranı
-- SELECT entity_id, COUNT(*) as total, SUM(CASE WHEN anomaly = 1 THEN 1 ELSE 0 END) as anomalies
-- FROM PredictionResults
-- WHERE ts > DATEADD(hour, -1, GETUTCDATE())
-- GROUP BY entity_id
-- ORDER BY anomalies DESC;
