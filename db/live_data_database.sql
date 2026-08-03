-- SCaDa live_data database layer
-- MySQL 8.0+; one full /api/SCaDa/live_data response equals one history row.
--
-- Calistirma sirasi: digital_twin_script.sql -> BU DOSYA -> regions_topology.sql
-- Cekirdek tablolar (entities, sensor_definitions, telemetry_readings) mevcutsa
-- sp_save_live_snapshot her snapshot'i ayrica narrow telemetri satirlarina acar
-- (CANLI_SIMULASYON_PLANI.md 2.3-A: "narrow depola, wide pivotla").
-- Cekirdek tablolar yoksa yalnizca snapshot katmani calisir.

-- Tum sema tek collation'da olmali (bkz. digital_twin_script.sql).
-- Kanonik collation = utf8mb4_0900_ai_ci; farkli collation'daki tablolar JOIN
-- edilince "Illegal mix of collations" hatasi olusur. SET NAMES ayrica
-- procedure govdesindeki literalleri istemciden bagimsiz kilar.
SET NAMES utf8mb4 COLLATE utf8mb4_0900_ai_ci;

CREATE DATABASE IF NOT EXISTS pipeline_digital_twin
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_0900_ai_ci;

USE pipeline_digital_twin;

CREATE TABLE IF NOT EXISTS live_snapshot_history (
    snapshot_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    captured_at DATETIME(6) NOT NULL,
    received_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    entity_count SMALLINT UNSIGNED NOT NULL,
    payload_bytes INT UNSIGNED NOT NULL,
    payload_json JSON NOT NULL,

    PRIMARY KEY (snapshot_id),
    UNIQUE KEY uq_live_snapshot_captured_at (captured_at),
    KEY idx_live_snapshot_received_at (received_at),
    CONSTRAINT chk_live_snapshot_object
        CHECK (JSON_TYPE(payload_json) = 'OBJECT')
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS live_entity_current (
    entity_id VARCHAR(100) NOT NULL,
    entity_type VARCHAR(40) NOT NULL,
    quality VARCHAR(20) NOT NULL DEFAULT 'GOOD',
    operating_status VARCHAR(30) NULL,
    sample_timestamp DATETIME(6) NOT NULL,
    latitude DECIMAL(10,7) NULL,
    longitude DECIMAL(10,7) NULL,
    payload_json JSON NOT NULL,
    snapshot_id BIGINT UNSIGNED NULL,
    updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
        ON UPDATE CURRENT_TIMESTAMP(6),

    PRIMARY KEY (entity_id),
    KEY idx_live_entity_type (entity_type),
    KEY idx_live_entity_sample_time (sample_timestamp),
    KEY idx_live_entity_snapshot (snapshot_id),
    CONSTRAINT fk_live_entity_snapshot
        FOREIGN KEY (snapshot_id)
        REFERENCES live_snapshot_history(snapshot_id)
        ON DELETE SET NULL,
    CONSTRAINT chk_live_entity_object
        CHECK (JSON_TYPE(payload_json) = 'OBJECT')
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_0900_ai_ci;

DROP PROCEDURE IF EXISTS sp_save_live_snapshot;

DELIMITER $$

CREATE PROCEDURE sp_save_live_snapshot (
    IN p_captured_at DATETIME(6),
    IN p_payload_json JSON
)
SQL SECURITY DEFINER
MODIFIES SQL DATA
BEGIN
    DECLARE v_snapshot_id BIGINT UNSIGNED;
    DECLARE v_entity_count INT UNSIGNED;
    DECLARE v_cutoff_snapshot_id BIGINT UNSIGNED DEFAULT NULL;
    DECLARE v_lock_acquired INT DEFAULT 0;
    DECLARE v_narrow_ready TINYINT DEFAULT 0;
    DECLARE v_telemetry_rows INT DEFAULT 0;

    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        IF v_lock_acquired = 1 THEN
            DO RELEASE_LOCK('pipeline_digital_twin.live_snapshot_write');
        END IF;
        RESIGNAL;
    END;

    IF p_captured_at IS NULL THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'captured_at is required';
    END IF;

    IF p_payload_json IS NULL
       OR JSON_TYPE(p_payload_json) <> 'OBJECT'
       OR JSON_LENGTH(p_payload_json) = 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'payload_json must be a non-empty JSON object';
    END IF;

    SELECT GET_LOCK('pipeline_digital_twin.live_snapshot_write', 10)
    INTO v_lock_acquired;

    IF v_lock_acquired <> 1 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'live snapshot writer lock could not be acquired';
    END IF;

    SET v_entity_count = JSON_LENGTH(p_payload_json);

    -- digital_twin_script.sql kurulmussa narrow telemetri katmani da beslenir.
    SELECT COUNT(*) = 3
    INTO v_narrow_ready
    FROM information_schema.tables
    WHERE table_schema = DATABASE()
      AND table_name IN ('entities', 'sensor_definitions', 'telemetry_readings');

    START TRANSACTION;

    INSERT INTO live_snapshot_history (
        captured_at,
        received_at,
        entity_count,
        payload_bytes,
        payload_json
    )
    VALUES (
        p_captured_at,
        CURRENT_TIMESTAMP(6),
        v_entity_count,
        OCTET_LENGTH(CAST(p_payload_json AS CHAR CHARACTER SET utf8mb4)),
        p_payload_json
    )
    ON DUPLICATE KEY UPDATE
        snapshot_id = LAST_INSERT_ID(snapshot_id),
        received_at = CURRENT_TIMESTAMP(6),
        entity_count = VALUES(entity_count),
        payload_bytes = VALUES(payload_bytes),
        payload_json = VALUES(payload_json);

    SET v_snapshot_id = LAST_INSERT_ID();

    INSERT INTO live_entity_current (
        entity_id,
        entity_type,
        quality,
        operating_status,
        sample_timestamp,
        latitude,
        longitude,
        payload_json,
        snapshot_id,
        updated_at
    )
    SELECT
        COALESCE(
            NULLIF(JSON_UNQUOTE(JSON_EXTRACT(source.entity_json, '$.entity_id')), ''),
            source.entity_key
        ) AS entity_id,
        COALESCE(
            NULLIF(JSON_UNQUOTE(JSON_EXTRACT(source.entity_json, '$.entity_type')), ''),
            'unknown'
        ) AS entity_type,
        COALESCE(
            NULLIF(JSON_UNQUOTE(JSON_EXTRACT(source.entity_json, '$.quality')), ''),
            'GOOD'
        ) AS quality,
        NULLIF(JSON_UNQUOTE(JSON_EXTRACT(source.entity_json, '$.status')), 'null')
            AS operating_status,
        COALESCE(
            STR_TO_DATE(
                JSON_UNQUOTE(JSON_EXTRACT(source.entity_json, '$.timestamp')),
                '%Y-%m-%dT%H:%i:%s.%f'
            ),
            p_captured_at
        ) AS sample_timestamp,
        CAST(JSON_UNQUOTE(JSON_EXTRACT(source.entity_json, '$.lat')) AS DECIMAL(10,7))
            AS latitude,
        CAST(JSON_UNQUOTE(JSON_EXTRACT(source.entity_json, '$.lon')) AS DECIMAL(10,7))
            AS longitude,
        source.entity_json,
        v_snapshot_id,
        CURRENT_TIMESTAMP(6)
    FROM (
        SELECT
            entity_keys.entity_key,
            JSON_EXTRACT(
                p_payload_json,
                CONCAT(
                    '$."',
                    REPLACE(entity_keys.entity_key, '"', '\\"'),
                    '"'
                )
            ) AS entity_json
        FROM JSON_TABLE(
            JSON_KEYS(p_payload_json),
            '$[*]' COLUMNS (
                entity_key VARCHAR(100) PATH '$'
            )
        ) AS entity_keys
    ) AS source
    WHERE JSON_TYPE(source.entity_json) = 'OBJECT'
    ON DUPLICATE KEY UPDATE
        entity_type = VALUES(entity_type),
        quality = VALUES(quality),
        operating_status = VALUES(operating_status),
        sample_timestamp = VALUES(sample_timestamp),
        latitude = VALUES(latitude),
        longitude = VALUES(longitude),
        payload_json = VALUES(payload_json),
        snapshot_id = VALUES(snapshot_id),
        updated_at = VALUES(updated_at);

    -- /live_data is a complete network snapshot; rows absent from the newest
    -- response are no longer considered current.
    DELETE FROM live_entity_current
    WHERE snapshot_id <> v_snapshot_id
       OR snapshot_id IS NULL;

    IF v_narrow_ready = 1 THEN
        -- Plan 2.3-A: canli varliklari entity kataloguna otomatik kaydet.
        INSERT INTO entities (
            entity_id,
            entity_type,
            display_name,
            is_active,
            created_at,
            status,
            quality
        )
        SELECT
            lec.entity_id,
            lec.entity_type,
            lec.entity_id,
            TRUE,
            CURRENT_TIMESTAMP(3),
            lec.operating_status,
            lec.quality
        FROM live_entity_current lec
        WHERE lec.snapshot_id = v_snapshot_id
        ON DUPLICATE KEY UPDATE
            status = VALUES(status),
            quality = VALUES(quality);

        -- Plan 2.3-A: snapshot'taki her sayisal tag = bir narrow satir.
        -- Verbose tag adi (orn. s_1_suction_pressure_bar) sensor_definitions
        -- uzerinden kanonik sensor_name'e (orn. s_1) eslenir; tanimsiz tag'ler
        -- ve sayisal olmayan alanlar (quality, timestamp, sensor_metadata,
        -- lat/lon) atlanir.
        --
        -- cycle: rolling feature penceresinin ekseni (GOREV_KISI4 Madde 5).
        -- Duvar saati yerine verinin kendi sirasi kullanilir. Kisi 2 payload'a
        -- gercek simulasyon cycle'ini eklerse ($.cycle) otomatik oradan alinir;
        -- yoksa monotonik snapshot_id proxy olarak kullanilir.
        INSERT INTO telemetry_readings (
            recorded_at,
            entity_id,
            entity_type,
            sensor_name,
            sensor_value,
            quality,
            ingestion_at,
            tag_name,
            cycle
        )
        SELECT
            lec.sample_timestamp,
            lec.entity_id,
            lec.entity_type,
            sd.sensor_name,
            CAST(JSON_UNQUOTE(JSON_EXTRACT(
                lec.payload_json,
                CONCAT('$."', tag.tag_key, '"')
            )) AS DECIMAL(18,6)),
            lec.quality,
            CURRENT_TIMESTAMP(3),
            tag.tag_key,
            COALESCE(
                CAST(JSON_UNQUOTE(JSON_EXTRACT(lec.payload_json, '$.cycle')) AS UNSIGNED),
                v_snapshot_id
            )
        FROM live_entity_current lec
        JOIN JSON_TABLE(
            JSON_KEYS(lec.payload_json),
            '$[*]' COLUMNS (
                tag_key VARCHAR(150) PATH '$'
            )
        ) AS tag
        INNER JOIN sensor_definitions sd
            ON sd.entity_type = lec.entity_type
            -- tag.tag_key JSON_TABLE kolonudur ve collation'i cagiran istemcinin
            -- oturum collation'ina gore degisebilir; kanonik 0900_ai_ci'ye
            -- sabitleyerek sema tablolariyla eslesmesini garanti ediyoruz.
            AND (sd.tag_name = tag.tag_key COLLATE utf8mb4_0900_ai_ci
                 OR sd.sensor_name = tag.tag_key COLLATE utf8mb4_0900_ai_ci)
            AND sd.is_active = TRUE
        WHERE lec.snapshot_id = v_snapshot_id
          AND JSON_TYPE(JSON_EXTRACT(
                  lec.payload_json,
                  CONCAT('$."', tag.tag_key, '"')
              )) IN ('INTEGER', 'DOUBLE', 'DECIMAL', 'UNSIGNED INTEGER')
        ON DUPLICATE KEY UPDATE
            sensor_value = VALUES(sensor_value),
            quality = VALUES(quality),
            ingestion_at = VALUES(ingestion_at),
            tag_name = VALUES(tag_name),
            cycle = VALUES(cycle);

        SET v_telemetry_rows = ROW_COUNT();
    END IF;

    -- Keep exactly the newest 1000 snapshot rows. The 1001st insert removes
    -- the oldest row in the same transaction.
    SET v_cutoff_snapshot_id = NULL;

    SELECT snapshot_id
    INTO v_cutoff_snapshot_id
    FROM live_snapshot_history
    ORDER BY snapshot_id DESC
    LIMIT 1 OFFSET 999;

    IF v_cutoff_snapshot_id IS NOT NULL THEN
        DELETE FROM live_snapshot_history
        WHERE snapshot_id < v_cutoff_snapshot_id;
    END IF;

    COMMIT;

    DO RELEASE_LOCK('pipeline_digital_twin.live_snapshot_write');
    SET v_lock_acquired = 0;

    SELECT
        v_snapshot_id AS snapshot_id,
        p_captured_at AS captured_at,
        v_entity_count AS entity_count,
        (SELECT COUNT(*) FROM live_snapshot_history) AS retained_snapshot_count,
        v_telemetry_rows AS narrow_telemetry_rows;
END$$

DELIMITER ;

CREATE OR REPLACE VIEW vw_live_snapshot_latest AS
SELECT
    history.snapshot_id,
    history.captured_at,
    history.received_at,
    history.entity_count,
    history.payload_bytes,
    history.payload_json
FROM live_snapshot_history AS history
INNER JOIN (
    SELECT MAX(snapshot_id) AS snapshot_id
    FROM live_snapshot_history
) AS latest
    ON latest.snapshot_id = history.snapshot_id;

CREATE OR REPLACE VIEW vw_live_database_status AS
SELECT
    DATABASE() AS database_name,
    (SELECT COUNT(*) FROM live_snapshot_history) AS retained_snapshot_count,
    (SELECT COUNT(*) FROM live_entity_current) AS current_entity_count,
    (SELECT MIN(captured_at) FROM live_snapshot_history) AS oldest_captured_at,
    (SELECT MAX(captured_at) FROM live_snapshot_history) AS latest_captured_at,
    (SELECT MAX(received_at) FROM live_snapshot_history) AS latest_received_at,
    (SELECT MAX(snapshot_id) FROM live_snapshot_history) AS latest_snapshot_id;

DROP PROCEDURE IF EXISTS sp_get_live_entity_history;

DELIMITER $$

CREATE PROCEDURE sp_get_live_entity_history (
    IN p_entity_id VARCHAR(100),
    IN p_row_limit INT
)
READS SQL DATA
BEGIN
    DECLARE v_limit INT DEFAULT 100;
    DECLARE v_json_path VARCHAR(220);

    IF p_entity_id IS NULL OR p_entity_id = '' THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'entity_id is required';
    END IF;

    SET v_limit = LEAST(GREATEST(COALESCE(p_row_limit, 100), 1), 1000);
    SET v_json_path = CONCAT(
        '$."',
        REPLACE(p_entity_id, '"', '\\"'),
        '"'
    );

    SELECT
        snapshot_id,
        captured_at,
        JSON_EXTRACT(payload_json, v_json_path) AS entity_payload
    FROM live_snapshot_history
    WHERE JSON_CONTAINS_PATH(payload_json, 'one', v_json_path) = 1
    ORDER BY snapshot_id DESC
    LIMIT v_limit;
END$$

DELIMITER ;

-- Application user should receive SELECT + EXECUTE only, not direct INSERT:
-- GRANT SELECT ON pipeline_digital_twin.live_snapshot_history TO 'SCaDa_live_writer'@'%';
-- GRANT SELECT ON pipeline_digital_twin.live_entity_current TO 'SCaDa_live_writer'@'%';
-- GRANT SELECT ON pipeline_digital_twin.vw_live_database_status TO 'SCaDa_live_writer'@'%';
-- GRANT EXECUTE ON PROCEDURE pipeline_digital_twin.sp_save_live_snapshot TO 'SCaDa_live_writer'@'%';
-- GRANT EXECUTE ON PROCEDURE pipeline_digital_twin.sp_get_live_entity_history TO 'SCaDa_live_writer'@'%';
