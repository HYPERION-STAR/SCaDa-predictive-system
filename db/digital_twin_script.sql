SELECT VERSION();

-- Tum sema TEK collation'da olmali; aksi halde farkli collation'daki tablolar
-- JOIN edilince "Illegal mix of collations" hatasi olusur (sp_save_live_snapshot
-- ve regions_topology.sql'in view'lari bu tablolari birlikte kullanir).
-- Kanonik collation = utf8mb4_0900_ai_ci (MySQL 8.0+ varsayilani).
-- SET NAMES ayrica procedure govdelerindeki literalleri ve JSON_TABLE
-- kolonlarini yukleyen istemciden bagimsiz kilar (latin1 istemci tuzagi).
SET NAMES utf8mb4 COLLATE utf8mb4_0900_ai_ci;

CREATE DATABASE IF NOT EXISTS pipeline_digital_twin
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_0900_ai_ci;
-- CREATE ... IF NOT EXISTS mevcut veritabaninda no-op oldugundan varsayilan
-- collation'i ALTER ile kesinlestiriyoruz (yeni tablolar bunu miras alir).
ALTER DATABASE pipeline_digital_twin
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_0900_ai_ci;
USE pipeline_digital_twin;

SELECT DATABASE();

-- =====================================================
-- ENTITY TABLOSU
-- =====================================================

CREATE TABLE IF NOT EXISTS entities (
    entity_id VARCHAR(50) NOT NULL,
    entity_type VARCHAR(30) NOT NULL,
    display_name VARCHAR(100),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at DATETIME,
    PRIMARY KEY (entity_id)
);

CREATE TABLE IF NOT EXISTS pipelines (
    pipeline_id VARCHAR(50) NOT NULL,
    pipeline_name VARCHAR(100) NOT NULL,
    product_type VARCHAR(20) NOT NULL,
    description VARCHAR(255),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    PRIMARY KEY (pipeline_id)
);

CREATE TABLE IF NOT EXISTS nodes (
    node_id VARCHAR(50) NOT NULL,
    node_name VARCHAR(100) NOT NULL,
    node_type VARCHAR(30) NOT NULL,
    latitude DECIMAL(9,6),
    longitude DECIMAL(9,6),
    elevation_m DECIMAL(8,2),
    design_pressure_bar DECIMAL(8,2),
    capacity_mcm_day DECIMAL(10,2),
    pipeline_id VARCHAR(50),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    PRIMARY KEY (node_id),
    CONSTRAINT fk_nodes_pipeline
        FOREIGN KEY (pipeline_id)
        REFERENCES pipelines(pipeline_id)
);

CREATE TABLE IF NOT EXISTS segments (
    segment_id VARCHAR(50) NOT NULL,
    from_node_id VARCHAR(50) NOT NULL,
    to_node_id VARCHAR(50) NOT NULL,
    pipeline_id VARCHAR(50) NOT NULL,

    product_type VARCHAR(20) NOT NULL,
    length_km DECIMAL(8,2),
    diameter_inch DECIMAL(6,2),
    wall_thickness_mm DECIMAL(6,2),
    design_pressure_bar DECIMAL(8,2),
    flow_direction VARCHAR(20),

    is_active BOOLEAN NOT NULL DEFAULT TRUE,

    PRIMARY KEY (segment_id),

    CONSTRAINT fk_segment_pipeline
        FOREIGN KEY (pipeline_id)
        REFERENCES pipelines(pipeline_id),

    CONSTRAINT fk_segment_from_node
        FOREIGN KEY (from_node_id)
        REFERENCES nodes(node_id),

    CONSTRAINT fk_segment_to_node
        FOREIGN KEY (to_node_id)
        REFERENCES nodes(node_id)
);

CREATE TABLE IF NOT EXISTS equipment (
    equipment_id VARCHAR(50) NOT NULL,
    node_id VARCHAR(50) NOT NULL,
    equipment_type VARCHAR(30) NOT NULL,
    model_name VARCHAR(100),
    rated_power_mw DECIMAL(8,2),
    install_date DATE,
    serial_number VARCHAR(100),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,

    PRIMARY KEY (equipment_id),

    CONSTRAINT fk_equipment_node
        FOREIGN KEY (node_id)
        REFERENCES nodes(node_id)
);
SHOW TABLES;

-- =====================================================
-- SENSOR TANIMLARI
-- Her varlık türünde kullanılabilecek sensörleri tanımlar.
-- =====================================================

CREATE TABLE IF NOT EXISTS sensor_definitions (
    entity_type VARCHAR(30) NOT NULL,
    sensor_name VARCHAR(80) NOT NULL,
    display_name VARCHAR(100) NOT NULL,
    unit_symbol VARCHAR(20),
    description VARCHAR(255),
    minimum_value DECIMAL(18,6),
    maximum_value DECIMAL(18,6),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,

    PRIMARY KEY (entity_type, sensor_name)
);

-- =====================================================
-- NARROW TELEMETRI TABLOSU
-- MQTT üzerinden gelen her sensör değeri ayrı satırdır.
-- =====================================================

CREATE TABLE IF NOT EXISTS telemetry_readings (
    telemetry_id BIGINT NOT NULL AUTO_INCREMENT,
    recorded_at DATETIME(3) NOT NULL,
    entity_id VARCHAR(50) NOT NULL,
    entity_type VARCHAR(30) NOT NULL,
    sensor_name VARCHAR(80) NOT NULL,
    sensor_value DECIMAL(18,6) NOT NULL,
    quality VARCHAR(20) NOT NULL,
    ingestion_at DATETIME(3),

    PRIMARY KEY (telemetry_id),

    CONSTRAINT fk_telemetry_entity
        FOREIGN KEY (entity_id)
        REFERENCES entities(entity_id),

    CONSTRAINT fk_telemetry_sensor
        FOREIGN KEY (entity_type, sensor_name)
        REFERENCES sensor_definitions(entity_type, sensor_name)
);

-- =====================================================
-- KESTIRIMCI BAKIM TAHMINLERI
-- RUL, sağlık durumu ve arıza modu sonuçları burada tutulur.
-- =====================================================

CREATE TABLE IF NOT EXISTS predictions (
    prediction_id BIGINT NOT NULL AUTO_INCREMENT,
    recorded_at DATETIME(3) NOT NULL,
    entity_id VARCHAR(50) NOT NULL,
    rul_value DECIMAL(12,2),
    health_state VARCHAR(30) NOT NULL,
    fault_mode VARCHAR(50),
    anomaly_score DECIMAL(8,6),
    model_version VARCHAR(50),
    created_at DATETIME(3),

    PRIMARY KEY (prediction_id),

    CONSTRAINT fk_prediction_entity
        FOREIGN KEY (entity_id)
        REFERENCES entities(entity_id)
);

-- =====================================================
-- ALARM KAYITLARI
-- Sızıntı, basınç, sıcaklık ve ekipman alarmları.
-- =====================================================

CREATE TABLE IF NOT EXISTS alarms (
    alarm_id BIGINT NOT NULL AUTO_INCREMENT,
    recorded_at DATETIME(3) NOT NULL,
    entity_id VARCHAR(50),
    alarm_type VARCHAR(50) NOT NULL,
    severity VARCHAR(20) NOT NULL,
    alarm_message VARCHAR(255) NOT NULL,
    alarm_status VARCHAR(20) NOT NULL DEFAULT 'OPEN',
    acknowledged_at DATETIME(3),
    resolved_at DATETIME(3),
    created_at DATETIME(3),

    PRIMARY KEY (alarm_id),

    CONSTRAINT fk_alarm_entity
        FOREIGN KEY (entity_id)
        REFERENCES entities(entity_id)
);

SHOW TABLES;

-- =====================================================
-- 1. PERFORMANS INDEKSLERI
-- API ve geçmiş telemetri sorgularını hızlandırır.
-- =====================================================

DROP PROCEDURE IF EXISTS add_index_if_missing;

DELIMITER $$

CREATE PROCEDURE add_index_if_missing (
    IN p_table_name VARCHAR(64),
    IN p_index_name VARCHAR(64),
    IN p_index_columns VARCHAR(255),
    IN p_is_unique BOOLEAN
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.statistics
        WHERE table_schema = DATABASE()
          AND table_name = p_table_name
          AND index_name = p_index_name
    ) THEN
        SET @sql_text = CONCAT(
            'CREATE ', IF(p_is_unique, 'UNIQUE ', ''),
            'INDEX `', p_index_name, '` ON `', p_table_name, '` (',
            p_index_columns, ')'
        );
        PREPARE stmt FROM @sql_text;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END$$

DELIMITER ;

CALL add_index_if_missing('nodes', 'idx_nodes_pipeline', '`pipeline_id`', FALSE);
CALL add_index_if_missing('segments', 'idx_segments_pipeline', '`pipeline_id`', FALSE);
CALL add_index_if_missing('segments', 'idx_segments_from_node', '`from_node_id`', FALSE);
CALL add_index_if_missing('segments', 'idx_segments_to_node', '`to_node_id`', FALSE);
CALL add_index_if_missing('equipment', 'idx_equipment_node', '`node_id`', FALSE);
CALL add_index_if_missing('telemetry_readings', 'idx_telemetry_entity_time', '`entity_id`, `recorded_at`', FALSE);
CALL add_index_if_missing('telemetry_readings', 'idx_telemetry_sensor_time', '`sensor_name`, `recorded_at`', FALSE);
CALL add_index_if_missing('telemetry_readings', 'idx_telemetry_recorded_at', '`recorded_at`', FALSE);

-- Once-only deduplication before enforcing the natural telemetry key.
DELETE duplicate_row
FROM telemetry_readings duplicate_row
INNER JOIN telemetry_readings keeper
    ON keeper.recorded_at = duplicate_row.recorded_at
    AND keeper.entity_id = duplicate_row.entity_id
    AND keeper.sensor_name = duplicate_row.sensor_name
    AND keeper.telemetry_id < duplicate_row.telemetry_id;

CALL add_index_if_missing('telemetry_readings', 'uq_telemetry_natural_key', '`recorded_at`, `entity_id`, `sensor_name`', TRUE);
CALL add_index_if_missing('predictions', 'idx_predictions_entity_time', '`entity_id`, `recorded_at`', FALSE);
CALL add_index_if_missing('predictions', 'idx_predictions_health_time', '`health_state`, `recorded_at`', FALSE);
CALL add_index_if_missing('predictions', 'uq_prediction_natural_key', '`recorded_at`, `entity_id`, `model_version`', TRUE);
CALL add_index_if_missing('alarms', 'idx_alarms_entity_time', '`entity_id`, `recorded_at`', FALSE);
CALL add_index_if_missing('alarms', 'idx_alarms_status_severity', '`alarm_status`, `severity`', FALSE);
CALL add_index_if_missing('alarms', 'uq_alarm_natural_key', '`recorded_at`, `entity_id`, `alarm_type`', TRUE);

DROP PROCEDURE IF EXISTS add_index_if_missing;

-- =====================================================
-- KOMPRESÖR SENSÖRLERİ - 21 ADET
-- =====================================================

INSERT IGNORE INTO sensor_definitions (
    entity_type,
    sensor_name,
    display_name,
    unit_symbol,
    description,
    minimum_value,
    maximum_value,
    is_active
)
VALUES
('compressor', 's_1',  'Suction Pressure',       'bar',  'Emme basıncı',                         0, 100, TRUE),
('compressor', 's_2',  'Discharge Pressure',     'bar',  'Basma basıncı',                        0, 150, TRUE),
('compressor', 's_3',  'Pressure Ratio',         'ratio','Basınç oranı',                         0, 10, TRUE),
('compressor', 's_4',  'Suction Temperature',    'C',    'Emme sıcaklığı',                     -50, 100, TRUE),
('compressor', 's_5',  'Discharge Temperature',  'C',    'Basma sıcaklığı',                    -50, 250, TRUE),
('compressor', 's_6',  'Shaft RPM',              'rpm',  'Mil dönüş hızı',                       0, 20000, TRUE),
('compressor', 's_7',  'Vibration DE',           'mm/s', 'Tahrik tarafı titreşim',               0, 50, TRUE),
('compressor', 's_8',  'Vibration NDE',          'mm/s', 'Tahrik dışı taraf titreşim',           0, 50, TRUE),
('compressor', 's_9',  'Axial Displacement',     'mm',   'Eksenel yer değiştirme',              -5, 5, TRUE),
('compressor', 's_10', 'Bearing Temperature 1',  'C',    'Birinci yatak sıcaklığı',            -20, 200, TRUE),
('compressor', 's_11', 'Bearing Temperature 2',  'C',    'İkinci yatak sıcaklığı',             -20, 200, TRUE),
('compressor', 's_12', 'Lube Oil Pressure',      'bar',  'Yağlama yağı basıncı',                 0, 20, TRUE),
('compressor', 's_13', 'Lube Oil Temperature',   'C',    'Yağlama yağı sıcaklığı',             -20, 150, TRUE),
('compressor', 's_14', 'Gas Flow Meter',         'mcm/d','Gaz debisi ölçümü',                    0, 100, TRUE),
('compressor', 's_15', 'Seal Gas Pressure',      'bar',  'Sızdırmazlık gazı basıncı',            0, 50, TRUE),
('compressor', 's_16', 'Gas Flow',               'mcm/d','Gaz akış miktarı',                     0, 100, TRUE),
('compressor', 's_17', 'Power',                  'MW',   'Kompresör güç tüketimi',                0, 100, TRUE),
('compressor', 's_18', 'Polytropic Efficiency',  '%',    'Politropik verim',                      0, 100, TRUE),
('compressor', 's_19', 'Surge Margin',           '%',    'Surge güvenlik marjı',                  0, 100, TRUE),
('compressor', 's_20', 'Filter Differential P',  'bar',  'Filtre diferansiyel basıncı',          0, 20, TRUE),
('compressor', 's_21', 'Torque',                 'Nm',   'Mil torku',                             0, 100000, TRUE);

-- =====================================================
-- SEGMENT SENSÖRLERİ - 8 ADET
-- =====================================================

INSERT IGNORE INTO sensor_definitions (
    entity_type,
    sensor_name,
    display_name,
    unit_symbol,
    description,
    minimum_value,
    maximum_value,
    is_active
)
VALUES
('segment', 's_inlet_pressure',   'Inlet Pressure',       'bar',  'Segment giriş basıncı',               0, 150, TRUE),
('segment', 's_outlet_pressure',  'Outlet Pressure',      'bar',  'Segment çıkış basıncı',               0, 150, TRUE),
('segment', 's_dp',               'Pressure Difference',  'bar',  'Basınç farkı',                         0, 100, TRUE),
('segment', 's_inlet_flow',       'Inlet Flow',           'mcm/d','Segment giriş debisi',                 0, 200, TRUE),
('segment', 's_outlet_flow',      'Outlet Flow',          'mcm/d','Segment çıkış debisi',                 0, 200, TRUE),
('segment', 's_mass_imbalance',   'Mass Imbalance',       '%',    'Giriş ve çıkış arasındaki fark',        0, 100, TRUE),
('segment', 's_temperature',      'Temperature',          'C',    'Hat içi sıcaklık',                    -50, 150, TRUE),
('segment', 's_cp_voltage',       'Cathodic Protection',  'V',    'Katodik koruma voltajı',               -5, 5, TRUE);

-- =====================================================
-- GİRİŞ NOKTASI SENSÖRLERİ - 4 ADET
-- =====================================================

INSERT IGNORE INTO sensor_definitions (
    entity_type,
    sensor_name,
    display_name,
    unit_symbol,
    description,
    minimum_value,
    maximum_value,
    is_active
)
VALUES
('node', 's_flow',           'Flow',            'mcm/d', 'Giriş debisi',          0, 200, TRUE),
('node', 's_pressure',       'Pressure',        'bar',   'Giriş basıncı',         0, 150, TRUE),
('node', 's_temperature',    'Temperature',     'C',     'Gaz sıcaklığı',        -50, 100, TRUE),
('node', 's_calorific_value','Calorific Value', 'MJ/m3', 'Kalorifik değer',       0, 60, TRUE);

-- =====================================================
-- KONTROL
-- =====================================================

SELECT
    entity_type,
    COUNT(*) AS sensor_count
FROM sensor_definitions
GROUP BY entity_type
ORDER BY entity_type;


-- =====================================================
-- 1. HER SENSÖRÜN EN SON TELEMETRİ DEĞERİ
-- =====================================================

CREATE OR REPLACE VIEW vw_latest_telemetry AS
SELECT
    telemetry_id,
    recorded_at,
    entity_id,
    entity_type,
    sensor_name,
    sensor_value,
    quality,
    ingestion_at
FROM (
    SELECT
        tr.*,
        ROW_NUMBER() OVER (
            PARTITION BY tr.entity_id, tr.sensor_name
            ORDER BY tr.recorded_at DESC, tr.telemetry_id DESC
        ) AS row_number_value
    FROM telemetry_readings tr
) ranked_telemetry
WHERE row_number_value = 1;

-- =====================================================
-- 2. HER EKİPMANIN EN SON TAHMİNİ
-- =====================================================

CREATE OR REPLACE VIEW vw_latest_predictions AS
SELECT
    prediction_id,
    recorded_at,
    entity_id,
    rul_value,
    health_state,
    fault_mode,
    anomaly_score,
    model_version,
    created_at
FROM (
    SELECT
        p.*,
        ROW_NUMBER() OVER (
            PARTITION BY p.entity_id
            ORDER BY p.recorded_at DESC, p.prediction_id DESC
        ) AS row_number_value
    FROM predictions p
) ranked_predictions
WHERE row_number_value = 1;

-- =====================================================
-- 3. AÇIK ALARMLAR
-- =====================================================

CREATE OR REPLACE VIEW vw_open_alarms AS
SELECT
    a.alarm_id,
    a.recorded_at,
    a.entity_id,
    e.entity_type,
    e.display_name,
    a.alarm_type,
    a.severity,
    a.alarm_message,
    a.alarm_status,
    a.acknowledged_at,
    a.created_at
FROM alarms a
LEFT JOIN entities e
    ON e.entity_id = a.entity_id
WHERE a.alarm_status = 'OPEN';

-- =====================================================
-- 4. SEGMENT VE BAĞLI DÜĞÜM BİLGİLERİ
-- =====================================================

CREATE OR REPLACE VIEW vw_network_segments AS
SELECT
    s.segment_id,
    s.pipeline_id,
    p.pipeline_name,
    p.product_type,

    s.from_node_id,
    from_node.node_name AS from_node_name,
    from_node.latitude AS from_latitude,
    from_node.longitude AS from_longitude,

    s.to_node_id,
    to_node.node_name AS to_node_name,
    to_node.latitude AS to_latitude,
    to_node.longitude AS to_longitude,

    s.length_km,
    s.diameter_inch,
    s.design_pressure_bar,
    s.flow_direction,
    s.is_active
FROM segments s
INNER JOIN pipelines p
    ON p.pipeline_id = s.pipeline_id
INNER JOIN nodes from_node
    ON from_node.node_id = s.from_node_id
INNER JOIN nodes to_node
    ON to_node.node_id = s.to_node_id;

-- =====================================================
-- 5. İSTASYON VE EKİPMAN BİLGİLERİ
-- =====================================================

CREATE OR REPLACE VIEW vw_station_equipment AS
SELECT
    n.node_id,
    n.node_name,
    n.node_type,
    n.latitude,
    n.longitude,
    n.pipeline_id,

    e.equipment_id,
    e.equipment_type,
    e.model_name,
    e.rated_power_mw,
    e.install_date,
    e.serial_number,
    e.is_active AS equipment_is_active
FROM nodes n
LEFT JOIN equipment e
    ON e.node_id = n.node_id;

-- =====================================================
-- KONTROL
-- =====================================================

SHOW FULL TABLES
WHERE Table_type = 'VIEW';

SELECT * FROM vw_latest_telemetry;
SELECT * FROM vw_latest_predictions;
SELECT * FROM vw_open_alarms;
SELECT * FROM vw_network_segments;
SELECT * FROM vw_station_equipment;

USE pipeline_digital_twin;

-- =====================================================
-- 1. VARLIK BAZLI TELEMETRİ GEÇMİŞİ
-- Başlangıç ve bitiş zamanı arasında sensör kayıtlarını getirir.
-- =====================================================

DROP PROCEDURE IF EXISTS sp_get_telemetry_history;

DELIMITER $$

CREATE PROCEDURE sp_get_telemetry_history (
    IN p_entity_id VARCHAR(50),
    IN p_start_time DATETIME(3),
    IN p_end_time DATETIME(3)
)
BEGIN
    SELECT
        tr.telemetry_id,
        tr.recorded_at,
        tr.entity_id,
        e.display_name,
        tr.entity_type,
        tr.sensor_name,
        sd.display_name AS sensor_display_name,
        tr.sensor_value,
        sd.unit_symbol,
        tr.quality,
        tr.ingestion_at
    FROM telemetry_readings tr
    INNER JOIN entities e
        ON e.entity_id = tr.entity_id
    LEFT JOIN sensor_definitions sd
        ON sd.entity_type = tr.entity_type
        AND sd.sensor_name = tr.sensor_name
    WHERE tr.entity_id = p_entity_id
      AND tr.recorded_at >= p_start_time
      AND tr.recorded_at <= p_end_time
    ORDER BY tr.recorded_at ASC, tr.telemetry_id ASC;
END$$

DELIMITER ;

-- =====================================================
-- 2. VARLIK VE SENSÖR BAZLI TELEMETRİ GEÇMİŞİ
-- Grafik çizimi için tek sensörün zaman serisini getirir.
-- =====================================================

DROP PROCEDURE IF EXISTS sp_get_sensor_history;

DELIMITER $$

CREATE PROCEDURE sp_get_sensor_history (
    IN p_entity_id VARCHAR(50),
    IN p_sensor_name VARCHAR(80),
    IN p_start_time DATETIME(3),
    IN p_end_time DATETIME(3)
)
BEGIN
    SELECT
        tr.recorded_at,
        tr.entity_id,
        tr.sensor_name,
        sd.display_name AS sensor_display_name,
        tr.sensor_value,
        sd.unit_symbol,
        tr.quality
    FROM telemetry_readings tr
    LEFT JOIN sensor_definitions sd
        ON sd.entity_type = tr.entity_type
        AND sd.sensor_name = tr.sensor_name
    WHERE tr.entity_id = p_entity_id
      AND tr.sensor_name = p_sensor_name
      AND tr.recorded_at >= p_start_time
      AND tr.recorded_at <= p_end_time
    ORDER BY tr.recorded_at ASC, tr.telemetry_id ASC;
END$$

DELIMITER ;

-- =====================================================
-- 3. TAHMİN GEÇMİŞİ
-- Bir ekipmanın RUL ve sağlık durumu geçmişini getirir.
-- =====================================================

DROP PROCEDURE IF EXISTS sp_get_prediction_history;

DELIMITER $$

CREATE PROCEDURE sp_get_prediction_history (
    IN p_entity_id VARCHAR(50),
    IN p_start_time DATETIME(3),
    IN p_end_time DATETIME(3)
)
BEGIN
    SELECT
        p.prediction_id,
        p.recorded_at,
        p.entity_id,
        e.display_name,
        p.rul_value,
        p.health_state,
        p.fault_mode,
        p.anomaly_score,
        p.model_version
    FROM predictions p
    INNER JOIN entities e
        ON e.entity_id = p.entity_id
    WHERE p.entity_id = p_entity_id
      AND p.recorded_at >= p_start_time
      AND p.recorded_at <= p_end_time
    ORDER BY p.recorded_at ASC, p.prediction_id ASC;
END$$

DELIMITER ;

-- =====================================================
-- 4. ALARM GEÇMİŞİ
-- Duruma ve önem seviyesine göre alarm kayıtlarını getirir.
-- NULL gönderilirse ilgili filtre uygulanmaz.
-- =====================================================

DROP PROCEDURE IF EXISTS sp_get_alarm_history;

DELIMITER $$

CREATE PROCEDURE sp_get_alarm_history (
    IN p_alarm_status VARCHAR(20),
    IN p_severity VARCHAR(20),
    IN p_start_time DATETIME(3),
    IN p_end_time DATETIME(3)
)
BEGIN
    SELECT
        a.alarm_id,
        a.recorded_at,
        a.entity_id,
        e.display_name,
        e.entity_type,
        a.alarm_type,
        a.severity,
        a.alarm_message,
        a.alarm_status,
        a.acknowledged_at,
        a.resolved_at
    FROM alarms a
    LEFT JOIN entities e
        ON e.entity_id = a.entity_id
    WHERE (p_alarm_status IS NULL OR a.alarm_status = p_alarm_status)
      AND (p_severity IS NULL OR a.severity = p_severity)
      AND a.recorded_at >= p_start_time
      AND a.recorded_at <= p_end_time
    ORDER BY a.recorded_at DESC, a.alarm_id DESC;
END$$

DELIMITER ;

-- =====================================================
-- KONTROL
-- =====================================================

SHOW PROCEDURE STATUS
WHERE Db = 'pipeline_digital_twin';

CALL sp_get_telemetry_history(
    'CS-KIRKLARELI-U1',
    '2026-07-14 00:00:00.000',
    '2026-07-14 23:59:59.999'
);

CALL sp_get_sensor_history(
    'CS-KIRKLARELI-U1',
    's_7',
    '2026-07-14 00:00:00.000',
    '2026-07-14 23:59:59.999'
);

CALL sp_get_prediction_history(
    'CS-KIRKLARELI-U2',
    '2026-07-14 00:00:00.000',
    '2026-07-14 23:59:59.999'
);

CALL sp_get_alarm_history(
    'OPEN',
    NULL,
    '2026-07-14 00:00:00.000',
    '2026-07-14 23:59:59.999'
);

-- =====================================================
-- KOMPRESÖR OPERASYONEL AYARLARI - 4 ADET
-- =====================================================

INSERT IGNORE INTO sensor_definitions (
    entity_type,
    sensor_name,
    display_name,
    unit_symbol,
    description,
    minimum_value,
    maximum_value,
    is_active
)
VALUES
(
    'compressor',
    'op_1',
    'Ambient Temperature',
    'C',
    'Ortam sıcaklığı',
    -50,
    70,
    TRUE
),
(
    'compressor',
    'op_2',
    'Inlet Pressure',
    'bar',
    'Kompresör giriş işletme basıncı',
    0,
    150,
    TRUE
),
(
    'compressor',
    'op_3',
    'Flow Demand',
    'mcm/d',
    'Talep edilen gaz debisi',
    0,
    200,
    TRUE
),
(
    'compressor',
    'op_4',
    'Speed Setpoint',
    'rpm',
    'Kompresör hız hedefi',
    0,
    20000,
    TRUE
);
-- =====================================================
-- 1. TELEMETRİ STAGING TABLOSU
-- MQTT Consumer gelen kayıtları önce buraya yazar.
-- =====================================================

CREATE TABLE IF NOT EXISTS telemetry_staging (
    staging_id BIGINT NOT NULL AUTO_INCREMENT,
    batch_id VARCHAR(50) NOT NULL,

    recorded_at DATETIME(3) NOT NULL,
    entity_id VARCHAR(50) NOT NULL,
    entity_type VARCHAR(30) NOT NULL,
    sensor_name VARCHAR(80) NOT NULL,
    sensor_value DECIMAL(18,6) NOT NULL,
    quality VARCHAR(20) NOT NULL,

    received_at DATETIME(3) NOT NULL,
    process_status VARCHAR(20) NOT NULL DEFAULT 'PENDING',
    error_message VARCHAR(255),

    PRIMARY KEY (staging_id)
);

-- =====================================================
-- 2. STAGING İNDEKSLERİ
-- =====================================================

DROP PROCEDURE IF EXISTS add_index_if_missing;

DELIMITER $$

CREATE PROCEDURE add_index_if_missing (
    IN p_table_name VARCHAR(64),
    IN p_index_name VARCHAR(64),
    IN p_index_columns VARCHAR(255),
    IN p_is_unique BOOLEAN
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.statistics
        WHERE table_schema = DATABASE()
          AND table_name = p_table_name
          AND index_name = p_index_name
    ) THEN
        SET @sql_text = CONCAT(
            'CREATE ', IF(p_is_unique, 'UNIQUE ', ''),
            'INDEX `', p_index_name, '` ON `', p_table_name, '` (',
            p_index_columns, ')'
        );
        PREPARE stmt FROM @sql_text;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END$$

DELIMITER ;

CALL add_index_if_missing('telemetry_staging', 'idx_staging_batch_status', '`batch_id`, `process_status`', FALSE);
CALL add_index_if_missing('telemetry_staging', 'idx_staging_entity_sensor', '`entity_id`, `sensor_name`', FALSE);
CALL add_index_if_missing('telemetry_staging', 'idx_staging_received_at', '`received_at`', FALSE);
CALL add_index_if_missing(
    'telemetry_staging',
    'uq_staging_batch_reading',
    '`batch_id`, `recorded_at`, `entity_id`, `sensor_name`',
    TRUE
);

DROP PROCEDURE IF EXISTS add_index_if_missing;

-- =====================================================
-- 3. BATCH AKTARIM PROCEDURE
-- Geçerli kayıtları ana telemetri tablosuna taşır.
-- =====================================================

DROP PROCEDURE IF EXISTS sp_process_telemetry_batch;

DELIMITER $$

CREATE PROCEDURE sp_process_telemetry_batch (
    IN p_batch_id VARCHAR(50)
)
BEGIN
    START TRANSACTION;

    -- Tanımsız entity kayıtlarını hatalı işaretle
    UPDATE telemetry_staging ts
    LEFT JOIN entities e
        ON e.entity_id = ts.entity_id
    SET
        ts.process_status = 'ERROR',
        ts.error_message = 'Entity definition not found'
    WHERE ts.batch_id = p_batch_id
      AND ts.process_status = 'PENDING'
      AND e.entity_id IS NULL;

    -- Entity tipi uyuşmayan kayıtları hatalı işaretle
    UPDATE telemetry_staging ts
    INNER JOIN entities e
        ON e.entity_id = ts.entity_id
    SET
        ts.process_status = 'ERROR',
        ts.error_message = 'Entity type mismatch'
    WHERE ts.batch_id = p_batch_id
      AND ts.process_status = 'PENDING'
      AND e.entity_type <> ts.entity_type;

    -- Tanımsız sensör kayıtlarını hatalı işaretle
    UPDATE telemetry_staging ts
    LEFT JOIN sensor_definitions sd
        ON sd.entity_type = ts.entity_type
        AND sd.sensor_name = ts.sensor_name
    SET
        ts.process_status = 'ERROR',
        ts.error_message = 'Sensor definition not found'
    WHERE ts.batch_id = p_batch_id
      AND ts.process_status = 'PENDING'
      AND sd.sensor_name IS NULL;

    -- Geçerli kayıtları ana tabloya topluca aktar
    INSERT INTO telemetry_readings (
        recorded_at,
        entity_id,
        entity_type,
        sensor_name,
        sensor_value,
        quality,
        ingestion_at
    )
    SELECT
        ts.recorded_at,
        ts.entity_id,
        ts.entity_type,
        ts.sensor_name,
        ts.sensor_value,
        ts.quality,
        ts.received_at
    FROM telemetry_staging ts
    WHERE ts.batch_id = p_batch_id
      AND ts.process_status = 'PENDING'
    ON DUPLICATE KEY UPDATE
        sensor_value = VALUES(sensor_value),
        quality = VALUES(quality),
        ingestion_at = VALUES(ingestion_at);

    -- Aktarılan kayıtları işlendi olarak işaretle
    UPDATE telemetry_staging
    SET
        process_status = 'PROCESSED',
        error_message = NULL
    WHERE batch_id = p_batch_id
      AND process_status = 'PENDING';

    COMMIT;
END$$

DELIMITER ;

-- =====================================================
-- 4. STAGING TEMİZLEME PROCEDURE
-- Eski işlenmiş kayıtları siler.
-- =====================================================

DROP PROCEDURE IF EXISTS sp_cleanup_telemetry_staging;

DELIMITER $$

CREATE PROCEDURE sp_cleanup_telemetry_staging (
    IN p_before_time DATETIME(3)
)
BEGIN
    DELETE FROM telemetry_staging
    WHERE received_at < p_before_time
      AND process_status IN ('PROCESSED', 'ERROR');
END$$

DELIMITER ;

-- =====================================================
-- SNAPSHOT JSON UYUMLULUK MIGRATION
-- Verilen ornek_veri.txt formatini desteklemek icin
-- mevcut tablo yapisini bozmadan genisletir.
-- =====================================================

CREATE TABLE IF NOT EXISTS regions (
    region_code VARCHAR(50) NOT NULL,
    region_label VARCHAR(150) NOT NULL,
    description TEXT,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
        ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (region_code)
);

DROP PROCEDURE IF EXISTS add_column_if_missing;
DROP PROCEDURE IF EXISTS add_index_if_missing;

DELIMITER $$

CREATE PROCEDURE add_column_if_missing (
    IN p_table_name VARCHAR(64),
    IN p_column_name VARCHAR(64),
    IN p_column_definition TEXT
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = DATABASE()
          AND table_name = p_table_name
          AND column_name = p_column_name
    ) THEN
        SET @sql_text = CONCAT(
            'ALTER TABLE `', p_table_name,
            '` ADD COLUMN `', p_column_name, '` ',
            p_column_definition
        );
        PREPARE stmt FROM @sql_text;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END$$

CREATE PROCEDURE add_index_if_missing (
    IN p_table_name VARCHAR(64),
    IN p_index_name VARCHAR(64),
    IN p_index_columns VARCHAR(255),
    IN p_is_unique BOOLEAN
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.statistics
        WHERE table_schema = DATABASE()
          AND table_name = p_table_name
          AND index_name = p_index_name
    ) THEN
        SET @sql_text = CONCAT(
            'CREATE ', IF(p_is_unique, 'UNIQUE ', ''), 'INDEX `', p_index_name,
            '` ON `', p_table_name, '` (', p_index_columns, ')'
        );
        PREPARE stmt FROM @sql_text;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END$$

DELIMITER ;

-- entities
CALL add_column_if_missing('entities', 'region_code', 'VARCHAR(50) NULL');
CALL add_column_if_missing('entities', 'status', 'VARCHAR(30) NULL');
CALL add_column_if_missing('entities', 'quality', 'VARCHAR(20) NULL');
CALL add_column_if_missing(
    'entities',
    'updated_at',
    'TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP'
);

-- nodes
CALL add_column_if_missing('nodes', 'region_code', 'VARCHAR(50) NULL');
CALL add_column_if_missing('nodes', 'equipment_count', 'INT NULL');
CALL add_column_if_missing('nodes', 'pipeline_ids_json', 'JSON NULL');

-- segments
CALL add_column_if_missing('segments', 'region_code', 'VARCHAR(50) NULL');
CALL add_column_if_missing('segments', 'max_capacity_mcm_day', 'DECIMAL(18,3) NULL');
CALL add_column_if_missing('segments', 'geometry_polyline', 'JSON NULL');

-- equipment
CALL add_column_if_missing('equipment', 'region_code', 'VARCHAR(50) NULL');
CALL add_column_if_missing('equipment', 'design_pressure_bar', 'DECIMAL(10,3) NULL');
CALL add_column_if_missing('equipment', 'flow_capacity_m3_h', 'DECIMAL(18,3) NULL');
CALL add_column_if_missing('equipment', 'status', 'VARCHAR(30) NULL');

-- sensor definitions
CALL add_column_if_missing('sensor_definitions', 'tag_name', 'VARCHAR(150) NULL');
CALL add_column_if_missing('sensor_definitions', 'tag_group', 'VARCHAR(30) NULL');

-- telemetry history
CALL add_column_if_missing('telemetry_readings', 'region_code', 'VARCHAR(50) NULL');
CALL add_column_if_missing('telemetry_readings', 'tag_name', 'VARCHAR(150) NULL');
CALL add_column_if_missing('telemetry_readings', 'mqtt_topic', 'VARCHAR(255) NULL');
-- cycle: rolling feature penceresinin ekseni (GOREV_KISI4 Madde 5).
-- Duvar saati (recorded_at) poll hizina bagli oldugu icin pencere ekseni
-- olarak kullanilmaz; cycle verinin kendi monotonik sirasidir.
-- sp_save_live_snapshot doldurur: payload'da $.cycle varsa (Kisi 2 eklerse)
-- ondan, yoksa snapshot_id proxy'sinden.
CALL add_column_if_missing('telemetry_readings', 'cycle', 'BIGINT NULL');

-- staging
CALL add_column_if_missing('telemetry_staging', 'region_code', 'VARCHAR(50) NULL');
CALL add_column_if_missing('telemetry_staging', 'tag_name', 'VARCHAR(150) NULL');
CALL add_column_if_missing('telemetry_staging', 'mqtt_topic', 'VARCHAR(255) NULL');

CREATE TABLE IF NOT EXISTS mqtt_raw_messages (
    raw_message_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    mqtt_topic VARCHAR(255) NOT NULL,
    payload_json JSON NULL,
    payload_text LONGTEXT,
    parse_status ENUM('SUCCESS', 'ERROR') NOT NULL,
    error_message VARCHAR(1000),
    received_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (raw_message_id)
);

CREATE TABLE IF NOT EXISTS ingestion_runs (
    ingestion_run_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    snapshot_timestamp DATETIME(6),
    simulation_cycle BIGINT,
    season_profile VARCHAR(100),
    ambient_temp_c DECIMAL(10,3),
    total_nodes INT,
    total_segments INT,
    total_equipment INT,
    total_telemetry_tags INT,
    mqtt_topic VARCHAR(255),
    inserted_reading_count INT NOT NULL DEFAULT 0,
    received_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (ingestion_run_id)
);

CALL add_index_if_missing('entities', 'idx_entities_region', '`region_code`', FALSE);
CALL add_index_if_missing('nodes', 'idx_nodes_region', '`region_code`', FALSE);
CALL add_index_if_missing('segments', 'idx_segments_region', '`region_code`', FALSE);
CALL add_index_if_missing('equipment', 'idx_equipment_region', '`region_code`', FALSE);
CALL add_index_if_missing('telemetry_readings', 'idx_telemetry_tag_time', '`tag_name`, `recorded_at`', FALSE);
CALL add_index_if_missing('telemetry_readings', 'idx_telemetry_region_time', '`region_code`, `recorded_at`', FALSE);
CALL add_index_if_missing('telemetry_readings', 'uq_telemetry_natural_key', '`recorded_at`, `entity_id`, `sensor_name`', TRUE);
-- vw_telemetry_features'in pencere fonksiyonlari icin: PARTITION BY entity_id,
-- sensor_name ORDER BY cycle -> bu indeks sirali okuma saglar.
CALL add_index_if_missing('telemetry_readings', 'idx_telemetry_entity_sensor_cycle', '`entity_id`, `sensor_name`, `cycle`', FALSE);
CALL add_index_if_missing('telemetry_staging', 'idx_staging_region_time', '`region_code`, `received_at`', FALSE);
CALL add_index_if_missing('mqtt_raw_messages', 'idx_raw_received_at', '`received_at`', FALSE);
CALL add_index_if_missing('mqtt_raw_messages', 'idx_raw_parse_status', '`parse_status`', FALSE);
CALL add_index_if_missing('ingestion_runs', 'idx_ingestion_snapshot_time', '`snapshot_timestamp`', FALSE);
CALL add_index_if_missing('ingestion_runs', 'uq_ingestion_snapshot_cycle', '`snapshot_timestamp`, `simulation_cycle`', TRUE);

-- Snapshot'taki verbose tag isimleri ile mevcut canonical sensor_name alanini esler.
UPDATE sensor_definitions
SET
    tag_name = 's_1_suction_pressure_bar',
    tag_group = 'sensor',
    unit_symbol = 'bar'
WHERE entity_type = 'compressor'
  AND sensor_name = 's_1'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_2_discharge_pressure_bar',
    tag_group = 'sensor',
    unit_symbol = 'bar'
WHERE entity_type = 'compressor'
  AND sensor_name = 's_2'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_3_pressure_ratio',
    tag_group = 'sensor',
    unit_symbol = 'ratio'
WHERE entity_type = 'compressor'
  AND sensor_name = 's_3'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_4_suction_temp_c',
    tag_group = 'sensor',
    unit_symbol = 'C'
WHERE entity_type = 'compressor'
  AND sensor_name = 's_4'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_5_discharge_temp_c',
    tag_group = 'sensor',
    unit_symbol = 'C'
WHERE entity_type = 'compressor'
  AND sensor_name = 's_5'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_6_shaft_rpm',
    tag_group = 'sensor',
    unit_symbol = 'rpm'
WHERE entity_type = 'compressor'
  AND sensor_name = 's_6'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_7_vibration_de_mm_s',
    tag_group = 'sensor',
    unit_symbol = 'mm/s'
WHERE entity_type = 'compressor'
  AND sensor_name = 's_7'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_8_vibration_nde_mm_s',
    tag_group = 'sensor',
    unit_symbol = 'mm/s'
WHERE entity_type = 'compressor'
  AND sensor_name = 's_8'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_9_axial_displacement_mm',
    tag_group = 'sensor',
    unit_symbol = 'mm'
WHERE entity_type = 'compressor'
  AND sensor_name = 's_9'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_10_bearing_temp_1_c',
    tag_group = 'sensor',
    unit_symbol = 'C'
WHERE entity_type = 'compressor'
  AND sensor_name = 's_10'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_11_bearing_temp_2_c',
    tag_group = 'sensor',
    unit_symbol = 'C'
WHERE entity_type = 'compressor'
  AND sensor_name = 's_11'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_12_lube_oil_pressure_bar',
    tag_group = 'sensor',
    unit_symbol = 'bar'
WHERE entity_type = 'compressor'
  AND sensor_name = 's_12'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_13_lube_oil_temp_c',
    tag_group = 'sensor',
    unit_symbol = 'C'
WHERE entity_type = 'compressor'
  AND sensor_name = 's_13'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_14_gas_flow_meter_m3_h',
    tag_group = 'sensor',
    unit_symbol = 'm3/h'
WHERE entity_type = 'compressor'
  AND sensor_name = 's_14'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_15_seal_gas_pressure_bar',
    tag_group = 'sensor',
    unit_symbol = 'bar'
WHERE entity_type = 'compressor'
  AND sensor_name = 's_15'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_16_gas_flow_m3_h',
    tag_group = 'sensor',
    unit_symbol = 'm3/h'
WHERE entity_type = 'compressor'
  AND sensor_name = 's_16'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_17_power_mw',
    tag_group = 'sensor',
    unit_symbol = 'MW'
WHERE entity_type = 'compressor'
  AND sensor_name = 's_17'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_18_polytropic_efficiency',
    tag_group = 'sensor',
    unit_symbol = 'ratio'
WHERE entity_type = 'compressor'
  AND sensor_name = 's_18'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_19_surge_margin_pct',
    tag_group = 'sensor',
    unit_symbol = '%'
WHERE entity_type = 'compressor'
  AND sensor_name = 's_19'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_20_filter_dp_bar',
    tag_group = 'sensor',
    unit_symbol = 'bar'
WHERE entity_type = 'compressor'
  AND sensor_name = 's_20'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_21_torque_nm',
    tag_group = 'sensor',
    unit_symbol = 'Nm'
WHERE entity_type = 'compressor'
  AND sensor_name = 's_21'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 'op_1_ambient_temp_c',
    tag_group = 'operation',
    unit_symbol = 'C'
WHERE entity_type = 'compressor'
  AND sensor_name = 'op_1'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 'op_2_inlet_pressure_bar',
    tag_group = 'operation',
    unit_symbol = 'bar'
WHERE entity_type = 'compressor'
  AND sensor_name = 'op_2'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 'op_3_flow_demand_m3_h',
    tag_group = 'operation',
    unit_symbol = 'm3/h'
WHERE entity_type = 'compressor'
  AND sensor_name = 'op_3'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 'op_4_speed_setpoint_pct',
    tag_group = 'operation',
    unit_symbol = '%'
WHERE entity_type = 'compressor'
  AND sensor_name = 'op_4'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_inlet_pressure_bar',
    tag_group = 'sensor',
    unit_symbol = 'bar'
WHERE entity_type = 'segment'
  AND sensor_name = 's_inlet_pressure'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_outlet_pressure_bar',
    tag_group = 'sensor',
    unit_symbol = 'bar'
WHERE entity_type = 'segment'
  AND sensor_name = 's_outlet_pressure'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_dp_bar',
    tag_group = 'sensor',
    unit_symbol = 'bar'
WHERE entity_type = 'segment'
  AND sensor_name = 's_dp'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_flow_m3_h',
    tag_group = 'sensor',
    unit_symbol = 'm3/h'
WHERE entity_type = 'segment'
  AND sensor_name = 's_inlet_flow'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_outflow_m3_h',
    tag_group = 'sensor',
    unit_symbol = 'm3/h'
WHERE entity_type = 'segment'
  AND sensor_name = 's_outlet_flow'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_mass_imbalance_pct',
    tag_group = 'sensor',
    unit_symbol = '%'
WHERE entity_type = 'segment'
  AND sensor_name = 's_mass_imbalance'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_temperature_c',
    tag_group = 'sensor',
    unit_symbol = 'C'
WHERE entity_type = 'segment'
  AND sensor_name = 's_temperature'
  AND (tag_name IS NULL OR tag_name = '');

UPDATE sensor_definitions
SET
    tag_name = 's_cathodic_protection_v',
    tag_group = 'sensor',
    unit_symbol = 'V'
WHERE entity_type = 'segment'
  AND sensor_name = 's_cp_voltage'
  AND (tag_name IS NULL OR tag_name = '');

INSERT INTO sensor_definitions (
    entity_type,
    sensor_name,
    display_name,
    unit_symbol,
    description,
    minimum_value,
    maximum_value,
    is_active,
    tag_name,
    tag_group
)
VALUES
('border_entry', 'flow_m3_h', 'Border Flow', 'm3/h', 'Sinir giris debisi', 0, 5000000, TRUE, 'flow_m3_h', 'sensor'),
('border_entry', 'pressure_bar', 'Border Pressure', 'bar', 'Sinir giris basinci', 0, 200, TRUE, 'pressure_bar', 'sensor'),
('border_entry', 'temperature_c', 'Border Temperature', 'C', 'Sinir giris gaz sicakligi', -50, 150, TRUE, 'temperature_c', 'sensor'),
('border_entry', 'calorific_value_mj_m3', 'Calorific Value', 'MJ/m3', 'Gaz kalorifik degeri', 0, 60, TRUE, 'calorific_value_mj_m3', 'sensor')
ON DUPLICATE KEY UPDATE
    display_name = VALUES(display_name),
    unit_symbol = VALUES(unit_symbol),
    description = VALUES(description),
    maximum_value = VALUES(maximum_value),
    tag_name = VALUES(tag_name),
    tag_group = VALUES(tag_group);

-- =====================================================
-- CANLI VERIDE BULUNAN DIGER VARLIK TIPLERI
-- /api/SCaDa/live_data cevabindaki ugs, fsru, lng_terminal
-- ve nokta tipli segment (offtake/junction) sensorleri.
-- Plan 2.1: depolama ~10 tag, sinir girisi ~4 tag.
-- =====================================================

INSERT INTO sensor_definitions (
    entity_type,
    sensor_name,
    display_name,
    unit_symbol,
    description,
    minimum_value,
    maximum_value,
    is_active,
    tag_name,
    tag_group
)
VALUES
('ugs', 's_storage_level_pct',      'Storage Level',        '%',       'Depo doluluk orani',              0, 100, TRUE, 's_storage_level_pct', 'sensor'),
('ugs', 's_inventory_mcm',          'Inventory',            'mcm',     'Depodaki gaz envanteri',          0, 10000, TRUE, 's_inventory_mcm', 'sensor'),
('ugs', 's_injection_flow_m3_h',    'Injection Flow',       'm3/h',    'Enjeksiyon debisi',               0, 5000000, TRUE, 's_injection_flow_m3_h', 'sensor'),
('ugs', 's_withdrawal_flow_m3_h',   'Withdrawal Flow',      'm3/h',    'Cekis debisi',                    0, 5000000, TRUE, 's_withdrawal_flow_m3_h', 'sensor'),
('ugs', 's_pressure_bar',           'Storage Pressure',     'bar',     'Depo basinci',                    0, 300, TRUE, 's_pressure_bar', 'sensor'),
('ugs', 's_cavern_temp_c',          'Cavern Temperature',   'C',       'Kaverna sicakligi',             -50, 150, TRUE, 's_cavern_temp_c', 'sensor'),
('ugs', 's_daily_capacity_mcm_day', 'Daily Capacity',       'mcm/day', 'Gunluk cekis kapasitesi',         0, 1000, TRUE, 's_daily_capacity_mcm_day', 'sensor'),
('ugs', 's_working_capacity_pct',   'Working Capacity',     '%',       'Kullanilabilir kapasite orani',   0, 100, TRUE, 's_working_capacity_pct', 'sensor'),

('fsru', 's_storage_level_pct', 'Storage Level',    '%',    'FSRU tank doluluk orani',      0, 100, TRUE, 's_storage_level_pct', 'sensor'),
('fsru', 's_pressure_bar',      'Send-out Pressure','bar',  'Gaz gonderim basinci',         0, 300, TRUE, 's_pressure_bar', 'sensor'),
('fsru', 's_boil_off_rate_pct', 'Boil-off Rate',    '%',    'LNG buharlasma orani',         0, 10, TRUE, 's_boil_off_rate_pct', 'sensor'),
('fsru', 's_liquid_flow_m3_h',  'Liquid Flow',      'm3/h', 'Sivi LNG debisi',              0, 100000, TRUE, 's_liquid_flow_m3_h', 'sensor'),
('fsru', 's_vapor_flow_m3_h',   'Vapor Flow',       'm3/h', 'Gazlastirilmis gaz debisi',    0, 5000000, TRUE, 's_vapor_flow_m3_h', 'sensor'),
('fsru', 's_tank_temp_c',       'Tank Temperature', 'C',    'LNG tank sicakligi',        -200, 50, TRUE, 's_tank_temp_c', 'sensor'),

('lng_terminal', 's_storage_level_pct', 'Storage Level',    '%',    'LNG tank doluluk orani',    0, 100, TRUE, 's_storage_level_pct', 'sensor'),
('lng_terminal', 's_pressure_bar',      'Send-out Pressure','bar',  'Gaz gonderim basinci',      0, 300, TRUE, 's_pressure_bar', 'sensor'),
('lng_terminal', 's_boil_off_rate_pct', 'Boil-off Rate',    '%',    'LNG buharlasma orani',      0, 10, TRUE, 's_boil_off_rate_pct', 'sensor'),
('lng_terminal', 's_regas_flow_m3_h',   'Regas Flow',       'm3/h', 'Gazlastirma debisi',        0, 5000000, TRUE, 's_regas_flow_m3_h', 'sensor'),
('lng_terminal', 's_tank_temp_c',       'Tank Temperature', 'C',    'LNG tank sicakligi',     -200, 50, TRUE, 's_tank_temp_c', 'sensor'),

-- Nokta tipli "segment" varliklari (offtake/junction): eski canli veride
-- entity_type=segment gelip boru sensorleri yerine su uc tag'i tasirdi.
-- (Guncel akista segmentler yalnizca boru sensorleri tasiyor; bu tanimlar
--  geriye donuk uyumluluk icin korunuyor, zararsiz.)
('segment', 'flow_m3_h',     'Point Flow',        'm3/h', 'Offtake/junction debisi',        0, 5000000, TRUE, 'flow_m3_h', 'sensor'),
('segment', 'pressure_bar',  'Point Pressure',    'bar',  'Offtake/junction basinci',       0, 200, TRUE, 'pressure_bar', 'sensor'),
('segment', 'temperature_c', 'Point Temperature', 'C',    'Offtake/junction gaz sicakligi', -50, 150, TRUE, 'temperature_c', 'sensor'),

-- Ham petrol pompa unitesi (oil_pump): PT-* / PS-* pompa istasyonlari (6 sensor).
-- C-MAPSS "motoru"nun petrol tarafindaki karsiligi -> kestirimci bakim hedefi.
('oil_pump', 's_suction_pressure_bar',   'Suction Pressure',   'bar',  'Pompa emme basinci',      0, 100, TRUE, 's_suction_pressure_bar', 'sensor'),
('oil_pump', 's_discharge_pressure_bar', 'Discharge Pressure', 'bar',  'Pompa basma basinci',     0, 150, TRUE, 's_discharge_pressure_bar', 'sensor'),
('oil_pump', 's_flow_m3_h',              'Flow',               'm3/h', 'Ham petrol debisi',       0, 50000, TRUE, 's_flow_m3_h', 'sensor'),
('oil_pump', 's_pump_power_mw',          'Pump Power',         'MW',   'Pompa guc tuketimi',      0, 100, TRUE, 's_pump_power_mw', 'sensor'),
('oil_pump', 's_bearing_temp_c',         'Bearing Temperature','C',    'Yatak sicakligi',       -20, 200, TRUE, 's_bearing_temp_c', 'sensor'),
('oil_pump', 's_vibration_mm_s',         'Vibration',          'mm/s', 'Titresim',                0, 50, TRUE, 's_vibration_mm_s', 'sensor'),

-- Ham petrol depolama (oil_storage): OILDEPO-* tanklari (4 sensor).
('oil_storage', 's_tank_level_pct',   'Tank Level',      '%',    'Tank doluluk orani',   0, 100, TRUE, 's_tank_level_pct', 'sensor'),
('oil_storage', 's_inventory_m3',     'Inventory',       'm3',   'Depodaki petrol hacmi',0, 10000000, TRUE, 's_inventory_m3', 'sensor'),
('oil_storage', 's_throughput_m3_h',  'Throughput',      'm3/h', 'Gecis debisi',         0, 100000, TRUE, 's_throughput_m3_h', 'sensor'),
('oil_storage', 's_tank_temp_c',      'Tank Temperature','C',    'Tank sicakligi',     -20, 100, TRUE, 's_tank_temp_c', 'sensor')
ON DUPLICATE KEY UPDATE
    display_name = VALUES(display_name),
    unit_symbol = VALUES(unit_symbol),
    description = VALUES(description),
    minimum_value = VALUES(minimum_value),
    maximum_value = VALUES(maximum_value),
    tag_name = VALUES(tag_name),
    tag_group = VALUES(tag_group);

-- =====================================================
-- NARROW TELEMETRI RETENTION
-- 5 sn'lik canli akista telemetry_readings hizla buyur
-- (~192 varlik x ~11 tag / 5 sn ~= 37M satir/gun).
-- Saatlik event 72 saatten eski satirlari partiler halinde siler.
-- =====================================================

DROP PROCEDURE IF EXISTS sp_prune_telemetry_readings;

DELIMITER $$

CREATE PROCEDURE sp_prune_telemetry_readings (
    IN p_keep_hours INT
)
MODIFIES SQL DATA
BEGIN
    DECLARE v_cutoff DATETIME(3);
    DECLARE v_deleted INT DEFAULT 1;

    SET v_cutoff = NOW(3) - INTERVAL LEAST(GREATEST(COALESCE(p_keep_hours, 72), 1), 8760) HOUR;

    WHILE v_deleted > 0 DO
        DELETE FROM telemetry_readings
        WHERE recorded_at < v_cutoff
        LIMIT 100000;
        SET v_deleted = ROW_COUNT();
    END WHILE;
END$$

DELIMITER ;

CREATE EVENT IF NOT EXISTS ev_prune_telemetry_readings
ON SCHEDULE EVERY 1 HOUR
COMMENT 'telemetry_readings tablosunda 72 saatlik pencere tutar'
DO CALL sp_prune_telemetry_readings(72);

-- =====================================================
-- ROLLING FEATURE VIEW  (GOREV_KISI4_VERITABANI.md Madde 5)
-- =====================================================
-- Amac: train/serve skew'ini kokten kesmek. Model 50 cevrimlik rmean/rstd/diff
-- istiyor. Bu pencere C#'ta RAM'de tutulursa (Dictionary<string, Queue<...>>)
-- uc sorun cikar:
--   1. Train/serve skew  - egitimde pandas, canlida C# Queue -> sessizce ayrisir
--   2. Pencere semantigi - RAM penceresi "son 50 poll" (duvar saati, poll hizina
--                          bagli); burasi "son 50 cycle" (verinin kendi zamani)
--   3. Restart'ta ucar   - 189 varlik x 50 kare RAM'de
-- Bu view egitim ve cikarimin ORTAK kaynagidir: Kisi 1 hem egitimde hem canlida
-- buradan okur.
--
-- Not (MySQL karsiliklari): SQL Server STDEV = ornek standart sapma ->
-- MySQL'de STDDEV_SAMP. Pencerenin ilk satirinda STDDEV_SAMP NULL doner
-- (pandas .std() ile ayni davranis: tek gozlemde ornek sapma tanimsiz).
-- diff'in ilk satiri da LAG yoklugundan NULL'dir.
CREATE OR REPLACE VIEW vw_telemetry_features AS
SELECT
    tr.entity_id,
    tr.entity_type,
    tr.sensor_name,
    tr.cycle,
    tr.recorded_at,
    tr.sensor_value AS value,
    AVG(tr.sensor_value) OVER w                       AS rmean,
    STDDEV_SAMP(tr.sensor_value) OVER w               AS rstd,
    tr.sensor_value - LAG(tr.sensor_value) OVER wd    AS diff,
    tr.quality
FROM telemetry_readings tr
WHERE tr.cycle IS NOT NULL
WINDOW
    w  AS (PARTITION BY tr.entity_id, tr.sensor_name
           ORDER BY tr.cycle
           ROWS BETWEEN 49 PRECEDING AND CURRENT ROW),
    wd AS (PARTITION BY tr.entity_id, tr.sensor_name
           ORDER BY tr.cycle);

-- Tek varlik/sensor icin pencere sorgusu (API/model tarafi icin indeks-dostu).
DROP PROCEDURE IF EXISTS sp_get_telemetry_features;

DELIMITER $$

CREATE PROCEDURE sp_get_telemetry_features (
    IN p_entity_id VARCHAR(50),
    IN p_sensor_name VARCHAR(80),
    IN p_row_limit INT
)
READS SQL DATA
BEGIN
    DECLARE v_limit INT DEFAULT 200;
    SET v_limit = LEAST(GREATEST(COALESCE(p_row_limit, 200), 1), 10000);

    SELECT *
    FROM (
        SELECT
            f.entity_id, f.sensor_name, f.cycle, f.recorded_at,
            f.value, f.rmean, f.rstd, f.diff, f.quality
        FROM vw_telemetry_features f
        WHERE f.entity_id = p_entity_id
          AND (p_sensor_name IS NULL OR f.sensor_name = p_sensor_name)
        ORDER BY f.cycle DESC
        LIMIT v_limit
    ) recent
    ORDER BY cycle ASC;
END$$

DELIMITER ;

-- API Hub icin indeks-dostu, salt okunur son telemetri sorgusu.
DROP PROCEDURE IF EXISTS sp_get_latest_telemetry;

DELIMITER $$

CREATE PROCEDURE sp_get_latest_telemetry (
    IN p_entity_id VARCHAR(50),
    IN p_region_code VARCHAR(50)
)
READS SQL DATA
BEGIN
    SELECT
        tr.telemetry_id,
        tr.recorded_at,
        tr.entity_id,
        e.display_name,
        tr.entity_type,
        tr.sensor_name,
        tr.tag_name,
        tr.sensor_value,
        sd.unit_symbol,
        tr.quality,
        tr.region_code,
        tr.mqtt_topic
    FROM telemetry_readings tr
    INNER JOIN (
        SELECT
            entity_id,
            sensor_name,
            MAX(recorded_at) AS latest_recorded_at
        FROM telemetry_readings
        WHERE (p_entity_id IS NULL OR entity_id = p_entity_id)
          AND (p_region_code IS NULL OR region_code = p_region_code)
        GROUP BY entity_id, sensor_name
    ) latest
        ON latest.entity_id = tr.entity_id
        AND latest.sensor_name = tr.sensor_name
        AND latest.latest_recorded_at = tr.recorded_at
    INNER JOIN entities e
        ON e.entity_id = tr.entity_id
    LEFT JOIN sensor_definitions sd
        ON sd.entity_type = tr.entity_type
        AND sd.sensor_name = tr.sensor_name
    WHERE (p_entity_id IS NULL OR tr.entity_id = p_entity_id)
      AND (p_region_code IS NULL OR tr.region_code = p_region_code)
    ORDER BY tr.entity_id, tr.sensor_name;
END$$

DELIMITER ;

DROP PROCEDURE IF EXISTS add_column_if_missing;
DROP PROCEDURE IF EXISTS add_index_if_missing;

