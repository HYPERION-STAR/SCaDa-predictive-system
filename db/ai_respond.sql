-- ai_respond — model servisinin /hub_state yanıtlarının ham log tablosu.
--
-- Kaynak: Kişi 1'in tahmin servisi (http://<host>:8010/hub_state)
-- Gövde şekli: { "nodes":[{id,health,rul,vibration,bearingTemp,dischargePressure}],
--                "segments":[{id,flow,load,leak}],
--                "updated_at":"2026-07-23T15:12:38" }
--
-- Tasarım notu: her poll turu TEK satır olur ve gövde `payload_json` içinde
-- olduğu gibi saklanır. Böylece hem nodes hem segments (farklı alan setleri)
-- tek log tablosuna sığar, hem de dışarı gönderirken yeniden serileştirme
-- gerekmez — satır aynen JSON olarak akıtılır. Düğüm bazlı sorgu gerekirse
-- MySQL 8.4 JSON_TABLE ile açılır (aşağıda örnek var).
--
-- Yükleme: mysql -uroot -p pipeline_digital_twin < db/ai_respond.sql

CREATE TABLE IF NOT EXISTS ai_respond (
  respond_id           BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,

  -- Toplama meta verisi
  fetched_at           DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  source_url           VARCHAR(255)    NOT NULL,
  http_status          SMALLINT UNSIGNED NOT NULL,
  latency_ms           INT UNSIGNED    NOT NULL,

  -- Modelin kendi bildirdiği zaman damgası (gövdedeki updated_at)
  model_updated_at     DATETIME(6)     NULL,

  -- Hızlı sorgu / sağlık kontrolü için özet sütunlar
  node_count           SMALLINT UNSIGNED NOT NULL DEFAULT 0,
  segment_count        SMALLINT UNSIGNED NOT NULL DEFAULT 0,
  critical_node_count  SMALLINT UNSIGNED NOT NULL DEFAULT 0,  -- health < 20
  leak_segment_count   SMALLINT UNSIGNED NOT NULL DEFAULT 0,  -- leak = true
  min_health           DECIMAL(7,3)    NULL,
  min_rul              DECIMAL(12,3)   NULL,

  -- Ham gövde + değişiklik tespiti için içerik özeti
  payload_bytes        INT UNSIGNED    NOT NULL,
  payload_sha256       CHAR(64)        NOT NULL,
  payload_json         JSON            NOT NULL,

  -- Dışarı gönderim (outbox) durumu
  send_status          VARCHAR(20)     NOT NULL DEFAULT 'pending',
  sent_at              DATETIME(6)     NULL,
  send_error           VARCHAR(500)    NULL,

  PRIMARY KEY (respond_id),
  KEY idx_ai_respond_fetched_at (fetched_at),
  KEY idx_ai_respond_model_updated (model_updated_at),
  KEY idx_ai_respond_outbox (send_status, respond_id),
  KEY idx_ai_respond_sha (payload_sha256),
  CONSTRAINT chk_ai_respond_object CHECK (JSON_TYPE(payload_json) = 'OBJECT'),
  CONSTRAINT chk_ai_respond_send_status
    CHECK (send_status IN ('pending', 'sent', 'failed', 'skipped'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- ---------------------------------------------------------------------------
-- Örnek sorgular
-- ---------------------------------------------------------------------------

-- Son turun düğümlerini satırlara aç:
--   SELECT n.* FROM ai_respond r
--   JOIN JSON_TABLE(r.payload_json, '$.nodes[*]' COLUMNS (
--          id                 VARCHAR(50)  PATH '$.id',
--          health             DECIMAL(7,3) PATH '$.health',
--          rul                DECIMAL(12,3) PATH '$.rul',
--          vibration          DECIMAL(9,3) PATH '$.vibration',
--          bearing_temp       DECIMAL(9,3) PATH '$.bearingTemp',
--          discharge_pressure DECIMAL(9,3) PATH '$.dischargePressure'
--        )) AS n
--   WHERE r.respond_id = (SELECT MAX(respond_id) FROM ai_respond);

-- Sızıntı bildiren segmentler:
--   SELECT r.respond_id, s.* FROM ai_respond r
--   JOIN JSON_TABLE(r.payload_json, '$.segments[*]' COLUMNS (
--          id   VARCHAR(80)   PATH '$.id',
--          flow DECIMAL(14,3) PATH '$.flow',
--          load_ratio DECIMAL(7,3) PATH '$.load',
--          leak TINYINT       PATH '$.leak'
--        )) AS s
--   WHERE s.leak = 1;
