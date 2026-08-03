using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SCaDaDashboard.Pipeline;

namespace SCaDaDashboard;

/// <summary>
/// Detay panelindeki tek sensör satırı. IsHeader=true grup başlığıdır;
/// Alert 0=normal, 1=uyarı, 2=kritik (değer rengi buna göre).
/// </summary>
public sealed record SensorRow(string Name, string Value, bool IsHeader = false, int Alert = 0);

/// <summary>Detay panelindeki ünite sağlık kırılımı satırı (pasta modu).</summary>
public sealed record UnitHealthRow(string Name, string Value, Brush Dot);

public partial class MapWindow : Window
{
    private IPipelineSource _source = null!;   // ApplySource ctor'da doldurur
    private Services.AppSettings _settings = Services.AppSettings.Load();
    private readonly DispatcherTimer _render;
    private int _renderTicks;

    private string? _selected;                         // acik detay istasyonu
    private readonly List<double> _vib = new(), _bt = new(), _dp = new();

    public MapWindow()
    {
        InitializeComponent();
        WindowFx.Apply(this); // koyu baslik cubugu + yuvarlak kose + belirme

        // Veri kaynagi: kalici ayar (settings.json) uygulanir; "Otomatik" modda
        // oncelik SCaDa_HUB_URL > snapshot dosyasi > simulasyon.
        ApplySource(_settings);
        InitHealthViewControls();
        Map.NodeClicked += OnNodeClicked;
        ThemeManager.Changed += OnThemeChanged; // tema degisince harita yeniden cizilir

        // ~16 fps render (akis animasyonu); simulasyon durumu saniyede 1 ilerler.
        _render = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _render.Tick += (_, _) =>
        {
            _renderTicks++;
            if (_renderTicks % 16 == 0)           // ~ her 1 sn
            {
                _source.Tick();
                Clock.Text = DateTime.Now.ToString("dd.MM.yyyy  HH:mm:ss");
                UpdateAlarms();
                // Sadece gaz istasyonu paneli periyodik tazelenir; pompa paneli statiktir.
                if (_selected != null && PipelineTopology.NodeById(_selected) is { IsStation: true }) UpdateDetail();
            }
            if (AlarmBox.Visibility == Visibility.Visible)   // yanip sonme
                AlarmBox.Opacity = 0.4 + 0.6 * (0.5 + 0.5 * Math.Sin(Environment.TickCount / 250.0));
            Map.InvalidateVisual();
        };
        UpdateAlarms();
        _render.Start();
    }

    // Secilen veri kaynagini kur ve UI'yi guncelle (baslangicta + ayar degisince).
    private void ApplySource(Services.AppSettings s)
    {
        string? snapshotPath = FindSnapshotFile();
        IPipelineSource src;
        string label;

        if (s.SourceMode == Services.SourceMode.Simulasyon)
        {
            PipelineTopology.ResetToDefault();
            src = new PipelineSimulator(); label = "SİMÜLASYON";
        }
        else if (s.SourceMode == Services.SourceMode.Snapshot
                 && snapshotPath != null && TryLoadSnapshot(snapshotPath, out var snap))
        {
            src = snap; label = "SNAPSHOT";
        }
        else if (s.SourceMode == Services.SourceMode.YerelVeri && TryLoadLocalData(out var local))
        {
            src = local; label = "YEREL VERİ";
        }
        else if (s.SourceMode == Services.SourceMode.Canli && s.LiveUrl.Length > 0
                 && (TryLoadApiLive(s.LiveUrl, s, out var live)
                     || (snapshotPath != null && TryLoadLive(snapshotPath, s.LiveUrl, s, out live))))
        {
            src = live; label = "CANLI";
        }
        else if (s.SourceMode == Services.SourceMode.ApiHub && s.HubUrl.Length > 0)
        {
            PipelineTopology.ResetToDefault();
            src = new HttpPipelineSource(s.HubUrl); label = "API HUB";
        }
        else // Otomatik (veya secilen kaynak kurulamadi)
        {
            var liveUrl = Environment.GetEnvironmentVariable("SCaDa_LIVE_URL");
            var hub = Environment.GetEnvironmentVariable("SCaDa_HUB_URL");
            if (!string.IsNullOrWhiteSpace(liveUrl)
                && (TryLoadApiLive(liveUrl, s, out var liveAuto)
                    || (snapshotPath != null && TryLoadLive(snapshotPath, liveUrl, s, out liveAuto))))
            {
                src = liveAuto; label = "CANLI";
            }
            else if (!string.IsNullOrWhiteSpace(hub))
            {
                PipelineTopology.ResetToDefault();
                src = new HttpPipelineSource(hub); label = "API HUB";
            }
            else if (snapshotPath != null && TryLoadSnapshot(snapshotPath, out var snap2))
            {
                src = snap2; label = "SNAPSHOT";
            }
            else
            {
                PipelineTopology.ResetToDefault();
                src = new PipelineSimulator(); label = "SİMÜLASYON";
            }
        }

        // Eski kaynagi kapat (ör. canli kaynagin SCaDaClient yoklama dongusu durur).
        if (!ReferenceEquals(_source, src) && _source is IDisposable old) old.Dispose();

        _source = src;
        SourceLabel.Text = $"  •  Veri kaynağı: {label}";
        UnitsButton.Visibility = _source is PipelineSimulator or SnapshotSource
            ? Visibility.Visible : Visibility.Collapsed;
        Map.Source = _source;
        Map.HealthMode = _settings.HealthDisplayMode;
        Map.OutlineAggregate = _settings.HealthOutlineAggregate;
        Map.CriticalThreshold = _settings.CriticalHealthThreshold;
        Map.SetPieSeparatorColorHex(_settings.PieSeparatorColorHex);
        BuildRegionBar(); // kaynak degisince bolge cubugu yenilenir

        // Acik detay eski kaynaga aitti; kapat.
        _selected = null;
        Map.SelectedId = null;
        Detail.Visibility = Visibility.Collapsed;
        UpdateAlarms();
        RefreshPieSepSwatch();
    }

    // Ayarlar penceresi: kaynak degisirse aninda uygula.
    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_settings, FindSnapshotFile(), FindLocalDataFiles() != null) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _settings = dlg.Result;
            ApplySource(_settings);
            // Esik degismis olabilir; rozet + acik panel tazelenir.
            UpdateAlarms();
            if (_selected != null && PipelineTopology.NodeById(_selected) is { IsStation: true })
                RenderStationHealth(_source.Node(_selected), _source.UnitHealths(_selected));
        }
    }

    // --- Saglik gorunumu (ust bar menusu) -----------------------------------

    private void InitHealthViewControls()
    {
        HvPie.IsChecked = _settings.HealthDisplayMode == HealthDisplayMode.Pie;
        HvAvg.IsChecked = _settings.HealthDisplayMode == HealthDisplayMode.Average;
        HvWorst.IsChecked = _settings.HealthDisplayMode == HealthDisplayMode.Worst;
        OaWorst.IsChecked = _settings.HealthOutlineAggregate == HealthAggregate.Worst;
        OaAvg.IsChecked = _settings.HealthOutlineAggregate == HealthAggregate.Average;
        UpdateOutlineSectionState();
        RefreshPieSepSwatch();
    }

    private void RefreshPieSepSwatch()
    {
        if (PieSepSwatch == null) return;
        var c = Map.EffectivePieSeparatorColor;
        PieSepSwatch.Background = new SolidColorBrush(c);
        bool custom = !string.IsNullOrWhiteSpace(_settings.PieSeparatorColorHex);
        PieSepHexText.Text = custom ? ColorUtil.ToHex(c) : $"Tema ({ColorUtil.ToHex(c)})";
    }

    private void PickPieSepColor_Click(object sender, RoutedEventArgs e)
    {
        var cur = Map.EffectivePieSeparatorColor;
        using var dlg = new System.Windows.Forms.ColorDialog
        {
            Color = System.Drawing.Color.FromArgb(cur.R, cur.G, cur.B),
            FullOpen = true,
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var picked = Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B);
        _settings.PieSeparatorColorHex = ColorUtil.ToHex(picked);
        Map.SetPieSeparatorColorHex(_settings.PieSeparatorColorHex);
        _settings.Save();
        RefreshPieSepSwatch();
    }

    private void ResetPieSepColor_Click(object sender, RoutedEventArgs e)
    {
        _settings.PieSeparatorColorHex = "";
        Map.SetPieSeparatorColorHex(null);
        _settings.Save();
        RefreshPieSepSwatch();
    }

    private void UpdateOutlineSectionState()
    {
        // Kenar rengi yalniz pasta modunda anlamli.
        if (OutlineSection != null)
            OutlineSection.IsEnabled = _settings.HealthDisplayMode == HealthDisplayMode.Pie;
    }

    private void HealthMode_Changed(object sender, RoutedEventArgs e)
    {
        if (Map == null) return; // XAML ayristirma sirasinda erken cagri korumasi
        var mode = HvAvg.IsChecked == true ? HealthDisplayMode.Average
                 : HvWorst.IsChecked == true ? HealthDisplayMode.Worst
                 : HealthDisplayMode.Pie;
        _settings.HealthDisplayMode = mode;
        Map.HealthMode = mode;
        UpdateOutlineSectionState();
        _settings.Save();
        if (_selected != null && PipelineTopology.NodeById(_selected) is { IsStation: true })
            RenderStationHealth(_source.Node(_selected), _source.UnitHealths(_selected));
    }

    private void OutlineAgg_Changed(object sender, RoutedEventArgs e)
    {
        if (Map == null) return;
        var agg = OaAvg.IsChecked == true ? HealthAggregate.Average : HealthAggregate.Worst;
        _settings.HealthOutlineAggregate = agg;
        Map.OutlineAggregate = agg;
        _settings.Save();
        if (_selected != null && PipelineTopology.NodeById(_selected) is { IsStation: true })
            RenderStationHealth(_source.Node(_selected), _source.UnitHealths(_selected));
    }

    // Gorunum moduna gore gosterilecek ozet saglik (baslik + durum kutusu).
    private double ShownHealth(IReadOnlyList<UnitHealth> units, double worstFallback) =>
        _settings.HealthDisplayMode == HealthDisplayMode.Average
            ? HealthAgg.Combine(units, HealthAggregate.Average, worstFallback)
            : _settings.HealthDisplayMode == HealthDisplayMode.Pie
                ? HealthAgg.Combine(units, _settings.HealthOutlineAggregate, worstFallback)
                : worstFallback;

    private void RenderStationHealth(NodeSnap snap, IReadOnlyList<UnitHealth> units)
    {
        double shown = ShownHealth(units, snap.Health);
        DetailRul.Text = $"{snap.Rul:0} döngü";
        DetailHealth.Text = $"%{shown:0}";
        // "KRİTİK" sınırı üst bar alarm rozetiyle aynı eşiği (CriticalHealthThreshold)
        // kullanır; eşik değişince panel ile rozet tutarlı kalır (aksi halde rozet
        // "KRİTİK" sayarken panel "RİSKLİ" gösterebiliyordu).
        bool kritik = shown < _settings.CriticalHealthThreshold;
        string durum = kritik ? "KRİTİK"
                     : shown >= 70 ? "SAĞLIKLI"
                     : shown >= 40 ? "UYARI" : "RİSKLİ";
        DetailStatus.Text = durum;
        DetailStatusBox.Background = new SolidColorBrush(
            kritik ? Color.FromRgb(0xE7, 0x4C, 0x3C) : HealthColorCs(shown));
        UpdateUnitBreakdown(units);
    }

    private void UpdateUnitBreakdown(IReadOnlyList<UnitHealth> units)
    {
        if (_settings.HealthDisplayMode == HealthDisplayMode.Pie && units.Count > 1)
        {
            UnitBreakdownList.ItemsSource = units.Select(u => new UnitHealthRow(
                u.UnitId,
                u.HasTelemetry ? $"%{u.Health:0}" : "veri yok",
                new SolidColorBrush(u.HasTelemetry ? HealthColorCs(u.Health)
                                                   : Color.FromRgb(0x5B, 0x6B, 0x7C)))).ToList();
            UnitBreakdownPanel.Visibility = Visibility.Visible;
        }
        else UnitBreakdownPanel.Visibility = Visibility.Collapsed;
    }

    private static Color HealthColorCs(double h) =>
        h >= 70 ? Color.FromRgb(0x2E, 0xCC, 0x71)
      : h >= 40 ? Color.FromRgb(0xF1, 0xC4, 0x0F)
      : h >= 20 ? Color.FromRgb(0xE6, 0x7E, 0x22)
      : Color.FromRgb(0xE7, 0x4C, 0x3C);

    // Tam ag snapshot dosyasini bul: SCaDa_SNAPSHOT env yolu, yoksa exe
    // klasorunden yukari dogru "*live_snapshot.json" aranir (dosya repo
    // kokunde, git disi tutulur).
    private static string? FindSnapshotFile()
    {
        var env = Environment.GetEnvironmentVariable("SCaDa_SNAPSHOT");
        if (!string.IsNullOrWhiteSpace(env) && System.IO.File.Exists(env)) return env;

        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            var hits = dir.GetFiles("*live_snapshot.json");
            if (hits.Length > 0) return hits[0].FullName;
        }
        return null;
    }

    // Yerel cikarilan topoloji dosyalarini bul: exe klasorunden yukari dogru
    // SCaDa_nodes.json + SCaDa_segments.json (ikisi de repo kokunde, git disi).
    private static (string Nodes, string Segments)? FindLocalDataFiles()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            var n = System.IO.Path.Combine(dir.FullName, "SCaDa_nodes.json");
            var s = System.IO.Path.Combine(dir.FullName, "SCaDa_segments.json");
            if (System.IO.File.Exists(n) && System.IO.File.Exists(s)) return (n, s);
        }
        return null;
    }

    // Yerel cikarilan topolojiyi yukle (yalniz topoloji; telemetri yok). Dosya
    // yoksa/bozuksa false -> ApplySource otomatige duser.
    private static bool TryLoadLocalData(out SnapshotSource source)
    {
        source = null!;
        var files = FindLocalDataFiles();
        if (files == null) return false;
        try
        {
            var data = LocalTopologyLoader.Load(files.Value.Nodes, files.Value.Segments);
            if (data.Nodes.Count == 0) return false;
            PipelineTopology.Load(data.Nodes, data.Segments);
            source = new SnapshotSource(data);
            return true;
        }
        catch
        {
            source = null!;
            return false;
        }
    }

    // Snapshot'i yukle; topolojiyi tam agla degistir. Bozuk dosyada sessizce
    // simulasyona dusulur (harita her kosulda calisir).
    private static bool TryLoadSnapshot(string path, out SnapshotSource source)
    {
        try
        {
            var data = SnapshotLoader.Load(path);
            if (data.Nodes.Count == 0) { source = null!; return false; }
            PipelineTopology.Load(data.Nodes, data.Segments);
            source = new SnapshotSource(data);
            return true;
        }
        catch
        {
            source = null!;
            return false;
        }
    }

    // Canli kaynak (API topolojisi): topolojiyi /nodes + /segments + /node/{id}
    // uclarindan kur; telemetriyi SCaDaClient (LiveDataService) kendi dongusunde
    // /live_data'dan ceker. Ayni sunucu oldugu icin id'ler birebir hizali
    // (snapshot dosyasi gerekmez). API kapaliysa false.
    private static bool TryLoadApiLive(string liveUrl, Services.AppSettings s, out LiveSnapshotSource source)
    {
        try
        {
            var baseUrl = new Uri(liveUrl).GetLeftPart(UriPartial.Authority);
            // Yüklemeyi havuz thread'inde çalıştır: UI thread'inde bloklu await deadlock'unu önler.
            var data = Task.Run(() => ApiSnapshotLoader.Load(baseUrl)).GetAwaiter().GetResult();
            if (data.Nodes.Count == 0) { source = null!; return false; }
            PipelineTopology.Load(data.Nodes, data.Segments);
            // poll aralığı + zaman aşımı ayarlardan geçer (istemci yoklamayi kendisi baslatir)
            source = new LiveSnapshotSource(data, baseUrl, s.PollIntervalSeconds, s.TimeoutSeconds);
            return true;
        }
        catch
        {
            source = null!;
            return false;
        }
    }

    // Canli kaynak (snapshot topolojisi, yedek): topolojiyi snapshot dosyasindan al,
    // telemetriyi canli endpoint'ten cek. Dosya/URL bozuksa sessizce dusulur.
    private static bool TryLoadLive(string path, string url, Services.AppSettings s, out LiveSnapshotSource source)
    {
        try
        {
            var data = SnapshotLoader.Load(path);
            if (data.Nodes.Count == 0) { source = null!; return false; }
            PipelineTopology.Load(data.Nodes, data.Segments);
            var baseUrl = new Uri(url).GetLeftPart(UriPartial.Authority);
            source = new LiveSnapshotSource(data, baseUrl, s.PollIntervalSeconds, s.TimeoutSeconds);
            return true;
        }
        catch
        {
            source = null!;
            return false;
        }
    }

    // Yuzen bolge cubugunu doldur: "Tümü" + her snapshot bolgesi icin cip.
    // Bolge bilgisi yoksa (sim/hub) cubuk gizlenir.
    private void BuildRegionBar()
    {
        RegionBar.Children.Clear();
        var present = new HashSet<string>(PipelineTopology.Nodes.Select(n => n.Region));
        present.Remove("");
        if (present.Count == 0) { RegionBarHost.Visibility = Visibility.Collapsed; return; }

        RegionBarHost.Visibility = Visibility.Visible;
        RegionBar.Children.Add(MakeRegionChip("Tümü", null));
        foreach (var (key, ad) in Pipeline.PipelineMapControl.Regions)
            if (present.Contains(key))
                RegionBar.Children.Add(MakeRegionChip(ad, key));
    }

    private System.Windows.Controls.Button MakeRegionChip(string text, string? regionKey)
    {
        var b = new System.Windows.Controls.Button
        {
            Content = text,
            Style = (System.Windows.Style)FindResource("GhostButton"),
            Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(2, 0, 2, 0),
            FontSize = 11,
        };
        b.Click += (_, _) =>
        {
            if (regionKey == null) Map.ResetView();
            else Map.FocusRegion(regionKey);
        };
        return b;
    }

    // Alt bardaki kategori onay kutusu: isaret kalkinca o tur haritada gizlenir.
    private void CategoryToggle(object sender, RoutedEventArgs e)
    {
        if (Map == null) return; // XAML parse sirasinda erken tetiklenme
        if (sender is not System.Windows.Controls.CheckBox cb || cb.Tag is not string cat) return;
        if (cb.IsChecked == true) Map.HiddenCategories.Remove(cat);
        else Map.HiddenCategories.Add(cat);
        Map.InvalidateVisual();
    }

    // Katman gorunurluk onay kutulari (ozellestirme).
    private void LayerProvinces(object sender, RoutedEventArgs e)
    { if (Map != null && sender is System.Windows.Controls.CheckBox c) Map.ShowProvinceBorders = c.IsChecked == true; }
    private void LayerGeo(object sender, RoutedEventArgs e)
    { if (Map != null && sender is System.Windows.Controls.CheckBox c) Map.ShowGeoLabels = c.IsChecked == true; }
    private void LayerFlow(object sender, RoutedEventArgs e)
    { if (Map != null && sender is System.Windows.Controls.CheckBox c) Map.ShowFlowArrows = c.IsChecked == true; }
    private void LayerGrid(object sender, RoutedEventArgs e)
    { if (Map != null && sender is System.Windows.Controls.CheckBox c) Map.ShowGrid = c.IsChecked == true; }

    // Genel makine panelini ac (5 jenerik makine).
    private void OpenDashboard_Click(object sender, RoutedEventArgs e) => new MainWindow().Show();

    // Kritik istasyonlari say, ust bar alarm rozetini guncelle.
    private void UpdateAlarms()
    {
        int crit = 0;
        foreach (var n in PipelineTopology.Nodes)
            if (n.IsStation && _source.Node(n.Id).Health < _settings.CriticalHealthThreshold) crit++;
        if (crit > 0)
        {
            AlarmText.Text = $"⚠  {crit} KRİTİK İSTASYON";
            AlarmBox.Visibility = Visibility.Visible;
        }
        else AlarmBox.Visibility = Visibility.Collapsed;
    }

    private void OnNodeClicked(PNode n)
    {
        // Tıklanabilir düğümler: gaz kompresör (IsStation, sağlık/sensör paneli),
        // ham petrol pompa/terminal (kimlik + bağlı hatlar) ve depolar. Her iki depo
        // tipi (gaz UGS + petrol deposu) haritada aynı silindir ikonuyla çizilir; bu
        // yüzden ikisi de aynı minimal depo panelini (doluluk + bağlı hatlar) açar.
        bool pump = n.Type is "PS" or "PT";
        bool storage = n.Type is "STORAGE" or "OILDEPO";
        if (!n.IsStation && !pump && !storage)
        {
            SelectedInfo.Text = $"{n.Id}  {n.Name}  —  {n.Type} (izleme dışı)";
            return;
        }

        _selected = n.Id;
        Map.SelectedId = n.Id; // haritada secim halkasi
        DetailId.Text = n.Id;
        DetailName.Text = n.Name;
        HealthPanel.Visibility = n.IsStation ? Visibility.Visible : Visibility.Collapsed;
        PumpPanel.Visibility = pump ? Visibility.Visible : Visibility.Collapsed;
        StoragePanel.Visibility = storage ? Visibility.Visible : Visibility.Collapsed;

        if (n.IsStation)
        {
            _vib.Clear(); _bt.Clear(); _dp.Clear();
            UpdateDetail();
            UpdateSensorList(n.Id);
            // Canli kaynak: B ucundan NodeDetail'i talep uzerine getir (fire-and-forget).
            DetailServerHealth.Visibility = Visibility.Collapsed;
            if (_source is LiveSnapshotSource live) _ = ShowServerHealthAsync(live, n.Id);
        }
        else if (storage)
        {
            ShowStorageDetail(n);
        }
        else
        {
            ShowPumpDetail(n);
        }

        ShowDetailAnimated();
        SelectedInfo.Text = $"{n.Id}  {n.Name}";
    }

    // Petrol pompa istasyonu / terminali detayi: telemetri yok — kimlik + bagli
    // ham petrol hatlari (btas.jpg'deki Pompa Istasyonu siniflandirmasina karsilik).
    private void ShowPumpDetail(PNode n)
    {
        DetailStatus.Text = "POMPA İSTASYONU";
        DetailStatusBox.Background = new SolidColorBrush(Color.FromRgb(0x9A, 0xBE, 0x3A));

        DetailPumpType.Text = "Ham Petrol Pompa İstasyonu";
        DetailPumpRegion.Text = System.Globalization.CultureInfo.CurrentCulture.TextInfo
            .ToTitleCase(n.Region.Replace('_', ' '));

        var sb = new System.Text.StringBuilder();
        int count = 0;
        foreach (var s in PipelineTopology.Segments)
        {
            if (s.From != n.Id && s.To != n.Id) continue;
            string otherId = s.From == n.Id ? s.To : s.From;
            var other = PipelineTopology.NodeById(otherId);
            if (other == null) continue; // uç düğüm topolojide yoksa satırı atla (çökme yerine)
            if (count > 0) sb.Append('\n');
            // Kapasite birimi ürüne göre: gaz hatları mcm/gün; petrol (oil) hattı bu gaz
            // birimini taşımaz — değeri birimsiz göster (ürün adı türü zaten belirtir).
            string kapasite = s.Product.StartsWith("gas", StringComparison.OrdinalIgnoreCase)
                ? $"{s.MaxCapacity:0} mcm/gün" : $"{s.MaxCapacity:0}";
            sb.Append($"→ {other.Name}   ({s.Product}, {kapasite})");
            count++;
        }
        DetailPumpConns.Text = count > 0 ? sb.ToString() : "Bağlı hat yok.";
    }

    // Depo minimal detayi (gaz UGS + petrol deposu ortak): doluluk + bagli hatlar.
    // Her iki depo tipi de canli doluluk yuzdesini (s_storage_level_pct) tasir.
    // Panel yapisi her iki depo tipinde ayni kalir; yalnizca kimlik rozeti degisir.
    private void ShowStorageDetail(PNode n)
    {
        bool oil = n.Type == "OILDEPO";
        DetailStatus.Text = oil ? "PETROL DEPO / YÜKLEME" : "DOĞAL GAZ DEPOLAMA (UGS)";
        DetailStatusBox.Background = new SolidColorBrush(oil
            ? Color.FromRgb(0xC8, 0xD1, 0x2E) : Color.FromRgb(0x4E, 0xCD, 0xC4));

        DetailStorageLevel.Text = $"%{_source.Level(n.Id):0}";

        var sb = new System.Text.StringBuilder();
        int count = 0;
        foreach (var s in PipelineTopology.Segments)
        {
            if (s.From != n.Id && s.To != n.Id) continue;
            string otherId = s.From == n.Id ? s.To : s.From;
            var other = PipelineTopology.NodeById(otherId);
            if (other == null) continue; // uç düğüm topolojide yoksa satırı atla (çökme yerine)
            if (count > 0) sb.Append('\n');
            // Kapasite birimi ürüne göre: gaz hatları mcm/gün; petrol (oil) hattı bu gaz
            // birimini taşımaz — değeri birimsiz göster (ürün adı türü zaten belirtir).
            string kapasite = s.Product.StartsWith("gas", StringComparison.OrdinalIgnoreCase)
                ? $"{s.MaxCapacity:0} mcm/gün" : $"{s.MaxCapacity:0}";
            sb.Append($"→ {other.Name}   ({s.Product}, {kapasite})");
            count++;
        }
        DetailStorageConns.Text = count > 0 ? sb.ToString() : "Bağlı hat yok.";
    }

    // Sunucunun yetkili health_state'ini canli B ucundan getirip detay panelinde
    // gosterir. Sonuc gelene kadar baska dugum secildiyse (veya panel kapandiysa)
    // yoksayilir. Bos/hata → gizli kalir, UI titresim proxy'sine guvenir.
    private async Task ShowServerHealthAsync(LiveSnapshotSource live, string nodeId)
    {
        string? state = await live.FetchNodeDetailAsync(nodeId);
        if (_selected != nodeId || string.IsNullOrWhiteSpace(state)) return;
        DetailServerHealth.Text = $"Sunucu değerlendirmesi: {state}";
        DetailServerHealth.Visibility = Visibility.Visible;
    }

    // Detay panelini sagdan kayarak + belirerek ac (zaten acisa animasyon yok).
    private void ShowDetailAnimated()
    {
        bool wasHidden = Detail.Visibility != Visibility.Visible;
        Detail.Visibility = Visibility.Visible;
        if (!wasHidden) return;
        var tt = (TranslateTransform)Detail.RenderTransform;
        tt.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(180))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        Detail.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    private void CloseDetail_Click(object sender, RoutedEventArgs e)
    {
        _selected = null;
        Map.SelectedId = null;
        Detail.Visibility = Visibility.Collapsed;
    }

    // Secili istasyonun unitelerini makine karti panelinde ac.
    private void OpenUnits_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        Services.IDataSource? units = _source switch
        {
            PipelineSimulator sim => new StationDataSource(sim, _selected),
            SnapshotSource snap => new SnapshotStationSource(snap, _selected),
            _ => null,
        };
        if (units != null)
            new MainWindow(units, $"{DetailName.Text} - Üniteler") { Owner = this }.Show();
    }

    private void UpdateDetail()
    {
        if (_selected == null) return;
        var snap = _source.Node(_selected);
        var s = _source.Sensors(_selected);
        Push(_vib, s.Vibration); Push(_bt, s.BearingTemp); Push(_dp, s.DischargePressure);
        VibChart.Values = _vib.ToArray(); BtChart.Values = _bt.ToArray(); DpChart.Values = _dp.ToArray();
        VibVal.Text = $"{s.Vibration:0.00} mm/s";
        BtVal.Text = $"{s.BearingTemp:0.0} °C";
        DpVal.Text = $"{s.DischargePressure:0.00} bar";
        RenderStationHealth(snap, _source.UnitHealths(_selected));

        // Telemetri kalitesi (canli kaynakta sunucudan): yalnizca GOOD disi durumda uyar.
        string quality = (_source as SnapshotSource)?.StationDataQuality(_selected) ?? "";
        if (quality.Length > 0 && !quality.Equals("GOOD", StringComparison.OrdinalIgnoreCase))
        {
            DetailQuality.Text = $"⚠ Telemetri kalitesi: {quality}";
            DetailQuality.Visibility = Visibility.Visible;
        }
        else DetailQuality.Visibility = Visibility.Collapsed;
    }

    // 21 kompresor sensoru: anahtar -> (Turkce ad, birim). Bilinmeyene jenerik ad.
    private static readonly Dictionary<string, (string Ad, string Birim)> SensorAdlari = new()
    {
        ["s_1_suction_pressure_bar"] = ("Emme Basıncı", "bar"),
        ["s_2_discharge_pressure_bar"] = ("Basma Basıncı", "bar"),
        ["s_3_pressure_ratio"] = ("Basınç Oranı", ""),
        ["s_4_suction_temp_c"] = ("Emme Sıcaklığı", "°C"),
        ["s_5_discharge_temp_c"] = ("Basma Sıcaklığı", "°C"),
        ["s_6_shaft_rpm"] = ("Şaft Devri", "rpm"),
        ["s_7_vibration_de_mm_s"] = ("Titreşim (DE)", "mm/s"),
        ["s_8_vibration_nde_mm_s"] = ("Titreşim (NDE)", "mm/s"),
        ["s_9_axial_displacement_mm"] = ("Eksenel Kayma", "mm"),
        ["s_10_bearing_temp_1_c"] = ("Yatak Sıcaklığı 1", "°C"),
        ["s_11_bearing_temp_2_c"] = ("Yatak Sıcaklığı 2", "°C"),
        ["s_12_lube_oil_pressure_bar"] = ("Yağlama Yağı Basıncı", "bar"),
        ["s_13_lube_oil_temp_c"] = ("Yağlama Yağı Sıcaklığı", "°C"),
        ["s_14_gas_flow_meter_m3_h"] = ("Gaz Akış Sayacı", "m³/sa"),
        ["s_15_seal_gas_pressure_bar"] = ("Sızdırmazlık Gazı Basıncı", "bar"),
        ["s_16_gas_flow_m3_h"] = ("Gaz Akışı", "m³/sa"),
        ["s_17_power_mw"] = ("Güç", "MW"),
        ["s_18_polytropic_efficiency"] = ("Politropik Verim", ""),
        ["s_19_surge_margin_pct"] = ("Surge Marjı", "%"),
        ["s_20_filter_dp_bar"] = ("Filtre ΔP", "bar"),
        ["s_21_torque_nm"] = ("Tork", "Nm"),
    };

    // Sensor gruplari (detay panelinde alt basliklar; duz liste yerine spec-sheet).
    private static readonly (string Baslik, string[] Anahtarlar)[] SensorGruplari =
    {
        ("BASINÇ", new[] { "s_1_suction_pressure_bar", "s_2_discharge_pressure_bar",
                           "s_3_pressure_ratio", "s_12_lube_oil_pressure_bar",
                           "s_15_seal_gas_pressure_bar", "s_20_filter_dp_bar" }),
        ("SICAKLIK", new[] { "s_4_suction_temp_c", "s_5_discharge_temp_c",
                             "s_10_bearing_temp_1_c", "s_11_bearing_temp_2_c",
                             "s_13_lube_oil_temp_c" }),
        ("TİTREŞİM & MEKANİK", new[] { "s_6_shaft_rpm", "s_7_vibration_de_mm_s",
                                       "s_8_vibration_nde_mm_s", "s_9_axial_displacement_mm",
                                       "s_21_torque_nm" }),
        ("AKIŞ & PERFORMANS", new[] { "s_14_gas_flow_meter_m3_h", "s_16_gas_flow_m3_h",
                                      "s_17_power_mw", "s_18_polytropic_efficiency",
                                      "s_19_surge_margin_pct" }),
    };

    // Nominal bant asimi esikleri: anahtar -> (uyari, kritik). Asan deger renklenir.
    private static readonly Dictionary<string, (double Uyari, double Kritik)> SensorEsikleri = new()
    {
        ["s_7_vibration_de_mm_s"] = (4.5, 7.1),   // ISO 10816 bolge sinirlarina yakin
        ["s_8_vibration_nde_mm_s"] = (4.5, 7.1),
        ["s_10_bearing_temp_1_c"] = (80, 95),
        ["s_11_bearing_temp_2_c"] = (80, 95),
        ["s_13_lube_oil_temp_c"] = (60, 75),
    };

    private static int AlertSeviyesi(string key, double v) =>
        SensorEsikleri.TryGetValue(key, out var e) ? (v > e.Kritik ? 2 : v > e.Uyari ? 1 : 0) : 0;

    // Detay panelinin sensor listesini doldur (yalnizca snapshot kaynaginda).
    private void UpdateSensorList(string stationId)
    {
        if (_source is SnapshotSource snap && snap.WorstUnitSensors(stationId) is { } wu)
        {
            SensorHeader.Text = $"TÜM SENSÖRLER — {wu.UnitId} (EN KÖTÜ ÜNİTE)";

            var rows = new List<SensorRow>();
            var kalan = wu.Sensors.Where(kv => kv.Key.StartsWith("s_"))
                                  .ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var (baslik, anahtarlar) in SensorGruplari)
            {
                var grup = anahtarlar.Where(kalan.ContainsKey).ToList();
                if (grup.Count == 0) continue;
                rows.Add(new SensorRow(baslik, "", IsHeader: true));
                foreach (var key in grup)
                {
                    rows.Add(SatirYap(key, kalan[key]));
                    kalan.Remove(key);
                }
            }
            if (kalan.Count > 0) // gruplara girmeyen (bilinmeyen) sensorler
            {
                rows.Add(new SensorRow("DİĞER", "", IsHeader: true));
                foreach (var kv in kalan.OrderBy(kv => SensorSira(kv.Key)))
                    rows.Add(SatirYap(kv.Key, kv.Value));
            }

            SensorList.ItemsSource = rows;
            SensorHeader.Visibility = Visibility.Visible;
            SensorList.Visibility = Visibility.Visible;
        }
        else
        {
            SensorHeader.Visibility = Visibility.Collapsed;
            SensorList.Visibility = Visibility.Collapsed;
        }
    }

    private static SensorRow SatirYap(string key, double deger)
    {
        var (ad, birim) = SensorAdlari.TryGetValue(key, out var s)
            ? s : (GenelSensorAdi(key), "");
        string metin = deger.ToString("0.##");
        return new SensorRow(ad, birim.Length > 0 ? $"{metin} {birim}" : metin,
            Alert: AlertSeviyesi(key, deger));
    }

    // "s_12_..." -> 12 (dosyadaki sensor sirasi korunur).
    private static int SensorSira(string key)
    {
        var parts = key.Split('_');
        return parts.Length > 1 && int.TryParse(parts[1], out int n) ? n : 99;
    }

    // Bilinmeyen anahtar icin okunur ad: "s_22_foo_bar" -> "foo bar".
    private static string GenelSensorAdi(string key)
    {
        var parts = key.Split('_');
        return string.Join(' ', parts.Skip(parts.Length > 1 && int.TryParse(parts[1], out _) ? 2 : 1));
    }

    private static void Push(List<double> buf, double v)
    {
        buf.Add(v);
        if (buf.Count > 60) buf.RemoveAt(0);
    }

    // Tema degisti: harita paleti guncellendi, yeniden ciz.
    private void OnThemeChanged()
    {
        Map.InvalidateVisual();
        RefreshPieSepSwatch();
    }

    protected override void OnClosed(EventArgs e)
    {
        _render.Stop();
        ThemeManager.Changed -= OnThemeChanged;
        base.OnClosed(e);
    }
}
