-- Otomatik uretildi: /api/SCaDa/nodes (121 dugum) — 2026-07-21 guncellemesi
SET NAMES utf8mb4 COLLATE utf8mb4_0900_ai_ci;
USE pipeline_digital_twin;

INSERT INTO regions (region_code, region_label) VALUES
  ('akdeniz', 'Akdeniz'),
  ('dogu_anadolu', 'Doğu Anadolu'),
  ('ege', 'Ege'),
  ('guneydogu_anadolu', 'Güneydoğu Anadolu'),
  ('ic_anadolu', 'İç Anadolu'),
  ('karadeniz', 'Karadeniz'),
  ('marmara', 'Marmara')
ON DUPLICATE KEY UPDATE region_label=VALUES(region_label);

SET @has_city := (SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema=DATABASE() AND table_name='nodes' AND column_name='city');
SET @sql := IF(@has_city=0, 'ALTER TABLE nodes ADD COLUMN city VARCHAR(80) NULL AFTER node_name', 'SELECT 1');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

INSERT INTO nodes (node_id, node_name, city, node_type, latitude, longitude, region_code, is_active) VALUES
  ('CS-KIRKLARELI-01', 'Kırklareli KS', 'KIRKLARELI', 'CS', 41.73, 27.22, 'marmara', TRUE),
  ('CS-AMBARLI-01', 'Ambarlı KS', 'AMBARLI', 'CS', 40.98, 28.69, 'marmara', TRUE),
  ('LNG-MARMARAEREGLISI-01', 'Marmara Ereğlisi LNG', 'MARMARAEREGLISI', 'LNG', 40.97, 27.95, 'marmara', TRUE),
  ('FSRU-SAROS-01', 'Saros FSRU', 'SAROS', 'FSRU', 40.55, 26.55, 'marmara', TRUE),
  ('UGS-SILIVRI-01', 'Silivri Yeraltı Depolama', 'SILIVRI', 'UGS', 41.1, 28.2, 'marmara', TRUE),
  ('BORDER-TURKSTREAM-01', 'TürkAkım Girişi (Kıyıköy)', 'TURKSTREAM', 'BORDER', 41.63, 28.1, 'marmara', TRUE),
  ('BORDER-GREECE-01', 'Yunanistan (İpsala)', 'GREECE', 'BORDER', 40.92, 26.38, 'marmara', TRUE),
  ('BORDER-TAP-01', 'TAP (İpsala)', 'TAP', 'BORDER', 40.85, 26.32, 'marmara', TRUE),
  ('OFFTAKE-ISTANBUL-01', 'İstanbul Çıkış', 'ISTANBUL', 'OFFTAKE', 41.01, 28.98, 'marmara', TRUE),
  ('OFFTAKE-KOCAELI-01', 'Kocaeli Çıkış', 'KOCAELI', 'OFFTAKE', 40.77, 29.92, 'marmara', TRUE),
  ('OFFTAKE-SAKARYA-01', 'Sakarya Çıkış', 'SAKARYA', 'OFFTAKE', 40.78, 30.4, 'marmara', TRUE),
  ('OFFTAKE-TEKIRDAG-01', 'Tekirdağ Çıkış', 'TEKIRDAG', 'OFFTAKE', 40.98, 27.51, 'marmara', TRUE),
  ('OFFTAKE-BURSA-01', 'Bursa Çıkış', 'BURSA', 'OFFTAKE', 40.19, 29.06, 'marmara', TRUE),
  ('OFFTAKE-BALIKESIR-01', 'Balıkesir Çıkış', 'BALIKESIR', 'OFFTAKE', 39.65, 27.89, 'marmara', TRUE),
  ('OFFTAKE-CANAKKALE-01', 'Çanakkale Çıkış', 'CANAKKALE', 'OFFTAKE', 40.15, 26.41, 'marmara', TRUE),
  ('OFFTAKE-BILECIK-01', 'Bilecik Çıkış', 'BILECIK', 'OFFTAKE', 40.14, 29.98, 'marmara', TRUE),
  ('OFFTAKE-EDIRNE-01', 'Edirne Çıkış', 'EDIRNE', 'OFFTAKE', 41.68, 26.56, 'marmara', TRUE),
  ('OFFTAKE-YALOVA-01', 'Yalova Çıkış', 'YALOVA', 'OFFTAKE', 40.66, 29.28, 'marmara', TRUE),
  ('OFFTAKE-GEBZE-01', 'Gebze Çıkış', 'GEBZE', 'OFFTAKE', 40.8, 29.43, 'marmara', TRUE),
  ('CS-ESKISEHIR-01', 'Eskişehir KS', 'ESKISEHIR', 'CS', 39.78, 30.52, 'ic_anadolu', TRUE),
  ('CS-MUCUR-01', 'Mucur KS (Kırşehir)', 'MUCUR', 'CS', 39.06, 34.38, 'ic_anadolu', TRUE),
  ('CS-SIVAS-01', 'Sivas KS', 'SIVAS', 'CS', 39.75, 37.02, 'ic_anadolu', TRUE),
  ('UGS-TUZGOLU-01', 'Tuz Gölü Yeraltı Depolama', 'TUZGOLU', 'UGS', 38.72, 33.38, 'ic_anadolu', TRUE),
  ('OFFTAKE-ANKARA-01', 'Ankara Çıkış', 'ANKARA', 'OFFTAKE', 39.93, 32.86, 'ic_anadolu', TRUE),
  ('OFFTAKE-KIRIKKALE-01', 'Kırıkkale Çıkış', 'KIRIKKALE', 'OFFTAKE', 39.85, 33.51, 'ic_anadolu', TRUE),
  ('OFFTAKE-KIRSEHIR-01', 'Kırşehir Çıkış', 'KIRSEHIR', 'OFFTAKE', 39.15, 34.16, 'ic_anadolu', TRUE),
  ('OFFTAKE-NEVSEHIR-01', 'Nevşehir Çıkış', 'NEVSEHIR', 'OFFTAKE', 38.62, 34.71, 'ic_anadolu', TRUE),
  ('OFFTAKE-AKSARAY-01', 'Aksaray Çıkış', 'AKSARAY', 'OFFTAKE', 38.37, 34.03, 'ic_anadolu', TRUE),
  ('OFFTAKE-NIGDE-01', 'Niğde Çıkış', 'NIGDE', 'OFFTAKE', 37.97, 34.68, 'ic_anadolu', TRUE),
  ('OFFTAKE-KAYSERI-01', 'Kayseri Çıkış', 'KAYSERI', 'OFFTAKE', 38.73, 35.48, 'ic_anadolu', TRUE),
  ('OFFTAKE-KONYA-01', 'Konya Çıkış', 'KONYA', 'OFFTAKE', 37.87, 32.48, 'ic_anadolu', TRUE),
  ('OFFTAKE-YOZGAT-01', 'Yozgat Çıkış', 'YOZGAT', 'OFFTAKE', 39.82, 34.81, 'ic_anadolu', TRUE),
  ('OFFTAKE-KARAMAN-01', 'Karaman Çıkış', 'KARAMAN', 'OFFTAKE', 37.18, 33.22, 'ic_anadolu', TRUE),
  ('PT-SIVAS-01', 'Sivas PT', 'SIVAS', 'PT', 39.55, 37.25, 'ic_anadolu', TRUE),
  ('OILDEPO-KIRIKKALE-01', 'Kırıkkale Ham Petrol Depolama', 'KIRIKKALE', 'OILDEPO', 39.88, 33.55, 'ic_anadolu', TRUE),
  ('CS-CORUM-01', 'Çorum KS', 'CORUM', 'CS', 40.55, 34.95, 'karadeniz', TRUE),
  ('BORDER-BLUESTREAM-01', 'Mavi Akım Girişi (Durusu)', 'BLUESTREAM', 'BORDER', 48.2, 39.68, 'karadeniz', TRUE),
  ('OFFTAKE-ZONGULDAK-01', 'Zonguldak Çıkış', 'ZONGULDAK', 'OFFTAKE', 41.45, 31.79, 'karadeniz', TRUE),
  ('OFFTAKE-BARTIN-01', 'Bartın Çıkış', 'BARTIN', 'OFFTAKE', 41.63, 32.34, 'karadeniz', TRUE),
  ('OFFTAKE-KARABUK-01', 'Karabük Çıkış', 'KARABUK', 'OFFTAKE', 41.2, 32.62, 'karadeniz', TRUE),
  ('OFFTAKE-KASTAMONU-01', 'Kastamonu Çıkış', 'KASTAMONU', 'OFFTAKE', 41.39, 33.78, 'karadeniz', TRUE),
  ('OFFTAKE-CANKIRI-01', 'Çankırı Çıkış', 'CANKIRI', 'OFFTAKE', 40.6, 33.62, 'karadeniz', TRUE),
  ('OFFTAKE-SAMSUN-01', 'Samsun Çıkış', 'SAMSUN', 'OFFTAKE', 41.29, 36.33, 'karadeniz', TRUE),
  ('OFFTAKE-AMASYA-01', 'Amasya Çıkış', 'AMASYA', 'OFFTAKE', 40.65, 35.83, 'karadeniz', TRUE),
  ('OFFTAKE-TOKAT-01', 'Tokat Çıkış', 'TOKAT', 'OFFTAKE', 40.31, 36.55, 'karadeniz', TRUE),
  ('OFFTAKE-ORDU-01', 'Ordu Çıkış', 'ORDU', 'OFFTAKE', 40.98, 37.88, 'karadeniz', TRUE),
  ('OFFTAKE-BOLU-01', 'Bolu Çıkış', 'BOLU', 'OFFTAKE', 40.74, 31.61, 'karadeniz', TRUE),
  ('OFFTAKE-DUZCE-01', 'Düzce Çıkış', 'DUZCE', 'OFFTAKE', 40.84, 31.16, 'karadeniz', TRUE),
  ('OFFTAKE-SINOP-01', 'Sinop Çıkış', 'SINOP', 'OFFTAKE', 42.03, 35.15, 'karadeniz', TRUE),
  ('OFFTAKE-FILYOS-01', 'Filyos Çıkış', 'FILYOS', 'OFFTAKE', 41.55, 32.02, 'karadeniz', TRUE),
  ('OFFTAKE-GIRESUN-01', 'Giresun Çıkış', 'GIRESUN', 'OFFTAKE', 40.91, 38.39, 'karadeniz', TRUE),
  ('OFFTAKE-TRABZON-01', 'Trabzon Çıkış', 'TRABZON', 'OFFTAKE', 41.0, 39.72, 'karadeniz', TRUE),
  ('OFFTAKE-RIZE-01', 'Rize Çıkış', 'RIZE', 'OFFTAKE', 41.02, 40.52, 'karadeniz', TRUE),
  ('OFFTAKE-GUMUSHANE-01', 'Gümüşhane Çıkış', 'GUMUSHANE', 'OFFTAKE', 40.46, 39.48, 'karadeniz', TRUE),
  ('OFFTAKE-ARTVIN-01', 'Artvin Çıkış', 'ARTVIN', 'OFFTAKE', 41.18, 41.82, 'karadeniz', TRUE),
  ('FIELD-SAKARYA-01', 'Sakarya Gaz Sahası', 'SAKARYA', 'FIELD', 41.9, 32.2, 'karadeniz', TRUE),
  ('CS-CAYIRLI-01', 'Çayırlı KS (Erzincan)', 'CAYIRLI', 'CS', 39.81, 40.05, 'dogu_anadolu', TRUE),
  ('CS-DOGUBAYAZIT-01', 'Doğubayazıt KS (Ağrı)', 'DOGUBAYAZIT', 'CS', 39.55, 44.08, 'dogu_anadolu', TRUE),
  ('BORDER-TANAP-01', 'TANAP Girişi (Türkgözü)', 'TANAP', 'BORDER', 41.45, 42.6, 'dogu_anadolu', TRUE),
  ('BORDER-IRAN-01', 'İran Girişi (Gürbulak)', 'IRAN', 'BORDER', 39.55, 44.3, 'dogu_anadolu', TRUE),
  ('BORDER-TURKMEN-01', 'Türkmenistan Girişi', 'TURKMEN', 'BORDER', 39.45, 44.35, 'dogu_anadolu', TRUE),
  ('BORDER-NAHCIVAN-01', 'Nahçıvan Çıkışı (Iğdır)', 'NAHCIVAN', 'BORDER', 39.92, 44.5, 'dogu_anadolu', TRUE),
  ('OFFTAKE-ERZURUM-01', 'Erzurum Çıkış', 'ERZURUM', 'OFFTAKE', 39.9, 41.27, 'dogu_anadolu', TRUE),
  ('OFFTAKE-ERZINCAN-01', 'Erzincan Çıkış', 'ERZINCAN', 'OFFTAKE', 39.75, 39.49, 'dogu_anadolu', TRUE),
  ('OFFTAKE-AGRI-01', 'Ağrı Çıkış', 'AGRI', 'OFFTAKE', 39.72, 43.05, 'dogu_anadolu', TRUE),
  ('OFFTAKE-IGDIR-01', 'Iğdır Çıkış', 'IGDIR', 'OFFTAKE', 39.92, 44.04, 'dogu_anadolu', TRUE),
  ('OFFTAKE-KARS-01', 'Kars Çıkış', 'KARS', 'OFFTAKE', 40.6, 43.09, 'dogu_anadolu', TRUE),
  ('OFFTAKE-VAN-01', 'Van Çıkış', 'VAN', 'OFFTAKE', 38.49, 43.38, 'dogu_anadolu', TRUE),
  ('OFFTAKE-MALATYA-01', 'Malatya Çıkış', 'MALATYA', 'OFFTAKE', 38.35, 38.31, 'dogu_anadolu', TRUE),
  ('OFFTAKE-ELAZIG-01', 'Elazığ Çıkış', 'ELAZIG', 'OFFTAKE', 38.68, 39.22, 'dogu_anadolu', TRUE),
  ('OFFTAKE-BAYBURT-01', 'Bayburt Çıkış', 'BAYBURT', 'OFFTAKE', 40.26, 40.22, 'dogu_anadolu', TRUE),
  ('OFFTAKE-ARDAHAN-01', 'Ardahan Çıkış', 'ARDAHAN', 'OFFTAKE', 41.11, 42.7, 'dogu_anadolu', TRUE),
  ('OFFTAKE-TUNCELI-01', 'Tunceli Çıkış', 'TUNCELI', 'OFFTAKE', 39.11, 39.55, 'dogu_anadolu', TRUE),
  ('OFFTAKE-BINGOL-01', 'Bingöl Çıkış', 'BINGOL', 'OFFTAKE', 38.88, 40.5, 'dogu_anadolu', TRUE),
  ('OFFTAKE-MUS-01', 'Muş Çıkış', 'MUS', 'OFFTAKE', 38.74, 41.49, 'dogu_anadolu', TRUE),
  ('OFFTAKE-BITLIS-01', 'Bitlis Çıkış', 'BITLIS', 'OFFTAKE', 38.4, 42.11, 'dogu_anadolu', TRUE),
  ('OFFTAKE-HAKKARI-01', 'Hakkari Çıkış', 'HAKKARI', 'OFFTAKE', 37.57, 43.74, 'dogu_anadolu', TRUE),
  ('PT-ARDAHAN-01', 'Ardahan PT', 'ARDAHAN', 'PT', 41.15, 42.75, 'dogu_anadolu', TRUE),
  ('PT-ERZURUM-01', 'Erzurum PT', 'ERZURUM', 'PT', 40.0, 41.35, 'dogu_anadolu', TRUE),
  ('BORDER-BTC-01', 'BTC Girişi (Türkgözü)', 'BTC', 'BORDER', 41.3, 42.85, 'dogu_anadolu', TRUE),
  ('LNG-ALIAGA-01', 'Aliağa LNG', 'ALIAGA', 'LNG', 38.8, 26.97, 'ege', TRUE),
  ('FSRU-ALIAGA-01', 'Aliağa FSRU', 'ALIAGA', 'FSRU', 38.79, 26.98, 'ege', TRUE),
  ('OFFTAKE-IZMIR-01', 'İzmir Çıkış', 'IZMIR', 'OFFTAKE', 38.42, 27.14, 'ege', TRUE),
  ('OFFTAKE-MANISA-01', 'Manisa Çıkış', 'MANISA', 'OFFTAKE', 38.61, 27.43, 'ege', TRUE),
  ('OFFTAKE-AYDIN-01', 'Aydın Çıkış', 'AYDIN', 'OFFTAKE', 37.85, 27.84, 'ege', TRUE),
  ('OFFTAKE-DENIZLI-01', 'Denizli Çıkış', 'DENIZLI', 'OFFTAKE', 37.78, 29.09, 'ege', TRUE),
  ('OFFTAKE-MUGLA-01', 'Muğla Çıkış', 'MUGLA', 'OFFTAKE', 37.22, 28.36, 'ege', TRUE),
  ('OFFTAKE-USAK-01', 'Uşak Çıkış', 'USAK', 'OFFTAKE', 38.68, 29.41, 'ege', TRUE),
  ('OFFTAKE-AFYON-01', 'Afyonkarahisar Çıkış', 'AFYON', 'OFFTAKE', 38.76, 30.54, 'ege', TRUE),
  ('OFFTAKE-KUTAHYA-01', 'Kütahya Çıkış', 'KUTAHYA', 'OFFTAKE', 39.42, 29.98, 'ege', TRUE),
  ('FSRU-DORTYOL-01', 'Dörtyol FSRU', 'DORTYOL', 'FSRU', 36.85, 36.22, 'akdeniz', TRUE),
  ('OFFTAKE-MERSIN-01', 'Mersin Çıkış', 'MERSIN', 'OFFTAKE', 36.81, 34.63, 'akdeniz', TRUE),
  ('OFFTAKE-ADANA-01', 'Adana Çıkış', 'ADANA', 'OFFTAKE', 37.0, 35.32, 'akdeniz', TRUE),
  ('OFFTAKE-OSMANIYE-01', 'Osmaniye Çıkış', 'OSMANIYE', 'OFFTAKE', 37.07, 36.25, 'akdeniz', TRUE),
  ('OFFTAKE-HATAY-01', 'Hatay Çıkış', 'HATAY', 'OFFTAKE', 36.2, 36.16, 'akdeniz', TRUE),
  ('OFFTAKE-ANTALYA-01', 'Antalya Çıkış', 'ANTALYA', 'OFFTAKE', 36.9, 30.71, 'akdeniz', TRUE),
  ('OFFTAKE-ISPARTA-01', 'Isparta Çıkış', 'ISPARTA', 'OFFTAKE', 37.76, 30.55, 'akdeniz', TRUE),
  ('OFFTAKE-BURDUR-01', 'Burdur Çıkış', 'BURDUR', 'OFFTAKE', 37.72, 30.29, 'akdeniz', TRUE),
  ('OFFTAKE-KAHRAMANMARAS-01', 'Kahramanmaraş Çıkış', 'KAHRAMANMARAS', 'OFFTAKE', 37.58, 36.93, 'akdeniz', TRUE),
  ('PS-OSMANIYE-01', 'Osmaniye PS', 'OSMANIYE', 'PS', 37.1, 36.3, 'akdeniz', TRUE),
  ('OILDEPO-CEYHAN-01', 'Ceyhan Ham Petrol Depolama', 'CEYHAN', 'OILDEPO', 36.87, 35.92, 'akdeniz', TRUE),
  ('OILDEPO-DORTYOL-01', 'Dörtyol Ham Petrol Depolama', 'DORTYOL', 'OILDEPO', 36.84, 36.21, 'akdeniz', TRUE),
  ('BORDER-SYRIA-01', 'Suriye Çıkışı (Kilis)', 'SYRIA', 'BORDER', 36.72, 37.12, 'guneydogu_anadolu', TRUE),
  ('BORDER-IRAQ-01', 'Irak (Habur)', 'IRAQ', 'BORDER', 37.2, 42.45, 'guneydogu_anadolu', TRUE),
  ('OFFTAKE-GAZIANTEP-01', 'Gaziantep Çıkış', 'GAZIANTEP', 'OFFTAKE', 37.07, 37.38, 'guneydogu_anadolu', TRUE),
  ('OFFTAKE-SANLIURFA-01', 'Şanlıurfa Çıkış', 'SANLIURFA', 'OFFTAKE', 37.17, 38.79, 'guneydogu_anadolu', TRUE),
  ('OFFTAKE-DIYARBAKIR-01', 'Diyarbakır Çıkış', 'DIYARBAKIR', 'OFFTAKE', 37.91, 40.24, 'guneydogu_anadolu', TRUE),
  ('OFFTAKE-MARDIN-01', 'Mardin Çıkış', 'MARDIN', 'OFFTAKE', 37.31, 40.74, 'guneydogu_anadolu', TRUE),
  ('OFFTAKE-BATMAN-01', 'Batman Çıkış', 'BATMAN', 'OFFTAKE', 37.88, 41.13, 'guneydogu_anadolu', TRUE),
  ('OFFTAKE-SIIRT-01', 'Siirt Çıkış', 'SIIRT', 'OFFTAKE', 37.93, 41.94, 'guneydogu_anadolu', TRUE),
  ('OFFTAKE-ADIYAMAN-01', 'Adıyaman Çıkış', 'ADIYAMAN', 'OFFTAKE', 37.76, 38.28, 'guneydogu_anadolu', TRUE),
  ('OFFTAKE-SIRNAK-01', 'Şırnak Çıkış', 'SIRNAK', 'OFFTAKE', 37.52, 42.46, 'guneydogu_anadolu', TRUE),
  ('OFFTAKE-KILIS-01', 'Kilis Çıkış', 'KILIS', 'OFFTAKE', 36.72, 37.11, 'guneydogu_anadolu', TRUE),
  ('PS-SILOPI-01', 'Silopi PS (Habur)', 'SILOPI', 'PS', 37.25, 42.47, 'guneydogu_anadolu', TRUE),
  ('PS-CIZRE-01', 'Cizre PS', 'CIZRE', 'PS', 37.33, 42.19, 'guneydogu_anadolu', TRUE),
  ('PS-MARDIN-01', 'Mardin PS', 'MARDIN', 'PS', 37.32, 40.9, 'guneydogu_anadolu', TRUE),
  ('PS-DIYARBAKIR-01', 'Diyarbakır PS', 'DIYARBAKIR', 'PS', 37.85, 40.3, 'guneydogu_anadolu', TRUE),
  ('PS-SANLIURFA-01', 'Şanlıurfa PS (Sarıl)', 'SANLIURFA', 'PS', 37.2, 38.85, 'guneydogu_anadolu', TRUE),
  ('PS-GAZIANTEP-01', 'Gaziantep PS', 'GAZIANTEP', 'PS', 37.1, 37.35, 'guneydogu_anadolu', TRUE),
  ('OILDEPO-BATMAN-01', 'Batman Ham Petrol Depolama', 'BATMAN', 'OILDEPO', 37.9, 41.15, 'guneydogu_anadolu', TRUE),
  ('OILDEPO-GABAR-01', 'Gabar Ham Petrol Depolama', 'GABAR', 'OILDEPO', 37.45, 42.15, 'guneydogu_anadolu', TRUE)
ON DUPLICATE KEY UPDATE
  node_name=VALUES(node_name), city=VALUES(city), node_type=VALUES(node_type),
  latitude=VALUES(latitude), longitude=VALUES(longitude), region_code=VALUES(region_code);

-- =====================================================
-- ZENGINLESTIRME + GRUPLAMA VIEW'LARI
-- Varliklari bolge / sehir / istasyon / tip bazinda gruplar.
-- entity_id'den sehir ve istasyon turetir, bolgeyi nodes/regions ile esler.
-- =====================================================

-- Sehir -> bolge haritasi (gercek dugumlerden)
CREATE OR REPLACE VIEW vw_city_region AS
SELECT city, MIN(region_code) AS region_code
FROM nodes
WHERE city IS NOT NULL
GROUP BY city;

-- Her varlik icin turetilmis boyutlar: sehir, istasyon dugumu, bolge
CREATE OR REPLACE VIEW vw_entity_dim AS
SELECT
    e.entity_id,
    e.entity_type,
    SUBSTRING_INDEX(SUBSTRING_INDEX(e.entity_id, '-', 2), '-', -1) AS city,
    CASE
        WHEN e.entity_type = 'compressor'
            THEN CONCAT('CS-', SUBSTRING_INDEX(SUBSTRING_INDEX(e.entity_id, '-', 2), '-', -1), '-01')
        WHEN e.entity_id LIKE 'SEG-%' THEN NULL
        ELSE e.entity_id
    END AS station_node_id,
    COALESCE(n.region_code, cr.region_code) AS region_code,
    r.region_label,
    n.node_name AS station_name
FROM entities e
LEFT JOIN nodes n
    ON n.node_id = CASE
        WHEN e.entity_type = 'compressor'
            THEN CONCAT('CS-', SUBSTRING_INDEX(SUBSTRING_INDEX(e.entity_id, '-', 2), '-', -1), '-01')
        WHEN e.entity_id LIKE 'SEG-%' THEN NULL
        ELSE e.entity_id
    END
LEFT JOIN vw_city_region cr
    ON cr.city = SUBSTRING_INDEX(SUBSTRING_INDEX(e.entity_id, '-', 2), '-', -1)
LEFT JOIN regions r
    ON r.region_code = COALESCE(n.region_code, cr.region_code)
WHERE e.is_active = TRUE;

-- Duzenli varlik katalogu (bolge > sehir > tip sirali listeleme)
CREATE OR REPLACE VIEW vw_entity_catalog AS
SELECT
    COALESCE(d.region_label, '(bilinmiyor)') AS bolge,
    d.region_code,
    d.city AS sehir,
    d.entity_type AS tip,
    d.station_node_id AS istasyon_dugumu,
    d.station_name AS istasyon_adi,
    d.entity_id AS varlik_id
FROM vw_entity_dim d;

-- Son snapshot: bolge x tip ozet sayilari
CREATE OR REPLACE VIEW vw_live_by_region AS
SELECT
    COALESCE(d.region_label, '(bilinmiyor)') AS bolge,
    d.region_code,
    d.entity_type AS tip,
    COUNT(*) AS varlik_sayisi
FROM live_entity_current lec
JOIN vw_entity_dim d ON d.entity_id = lec.entity_id
GROUP BY d.region_label, d.region_code, d.entity_type;

-- Son snapshot: istasyon bazinda kompresor unite sayilari
CREATE OR REPLACE VIEW vw_live_by_station AS
SELECT
    COALESCE(d.region_label, '(bilinmiyor)') AS bolge,
    d.city AS sehir,
    d.station_node_id AS istasyon_dugumu,
    d.station_name AS istasyon_adi,
    COUNT(DISTINCT lec.entity_id) AS unite_sayisi
FROM live_entity_current lec
JOIN vw_entity_dim d ON d.entity_id = lec.entity_id
WHERE d.entity_type = 'compressor'
GROUP BY d.region_label, d.city, d.station_node_id, d.station_name;

-- Ana nizami listeleme: son telemetri degerleri + bolge/sehir/istasyon/sensor etiketi
CREATE OR REPLACE VIEW vw_live_telemetry_enriched AS
SELECT
    COALESCE(d.region_label, '(bilinmiyor)') AS bolge,
    d.region_code,
    d.city AS sehir,
    d.entity_type AS tip,
    d.station_node_id AS istasyon_dugumu,
    d.station_name AS istasyon_adi,
    t.entity_id AS varlik_id,
    t.sensor_name,
    sd.display_name AS sensor_adi,
    t.sensor_value AS deger,
    sd.unit_symbol AS birim,
    t.quality,
    t.recorded_at
FROM vw_latest_telemetry t
JOIN vw_entity_dim d ON d.entity_id = t.entity_id
LEFT JOIN sensor_definitions sd
    ON sd.entity_type = t.entity_type AND sd.sensor_name = t.sensor_name;




-- Bölge × varlık tipi özeti
SELECT * FROM vw_live_by_region ORDER BY bolge, tip;

-- İstasyon bazında kompresör üniteleri
SELECT * FROM vw_live_by_station ORDER BY bolge, sehir;

-- Düzenli varlık katalogu (bölge > şehir > tip)
SELECT * FROM vw_entity_catalog ORDER BY bolge, sehir, tip;

-- Ana telemetri listesi: değer + bölge/şehir/istasyon/sensör etiketi
SELECT bolge, sehir, istasyon_adi, varlik_id, sensor_adi, deger, birim, recorded_at
FROM vw_live_telemetry_enriched
WHERE bolge='İç Anadolu' AND tip='compressor';
