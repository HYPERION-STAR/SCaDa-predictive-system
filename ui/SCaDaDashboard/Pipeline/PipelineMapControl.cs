using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SCaDaDashboard.Pipeline;

/// <summary>
/// Boru hattı ağını çizen canlı harita kontrolü. Arka planda kendi vektör
/// Türkiye haritamızı (gömülü il sınırları) çizer; üstüne segmentleri doluluğa
/// göre renklendirip akış yönünde kayan noktalarla canlandırır ve düğümleri
/// sağlık durumuna göre renklendirir. Harita ve düğümler aynı coğrafi
/// projeksiyonu kullandığından hizalama otomatiktir. Harici NuGet gerekmez.
/// </summary>
public sealed class PipelineMapControl : Control
{
    // Tema-bagimli renkler (ThemeManager.ApplyTheme ile koyu/acik arasinda gecer).
    private static Color Bg = C(0x0F, 0x16, 0x20);        // notr zemin (dugum kenari, etiket arkasi)
    private static Color Sea = C(0x0D, 0x24, 0x3A);       // deniz (belirgin mavi)
    private static Color Land = C(0x1B, 0x27, 0x33);      // kara dolgusu (Turkiye)
    private static Color NeighLand = C(0x14, 0x1D, 0x29); // komsu ulke
    private static Color NeighEdge = C(0x3C, 0x4E, 0x60); // kiyi/sinir cizgisi
    private static Color Province = C(0x36, 0x45, 0x55);  // il sınırı
    private static Color TextCol = C(0xE6, 0xEE, 0xF6);
    private static Color MutedCol = C(0x9A, 0xAF, 0xC4);
    private static Color FlowDash = C(0xDF, 0xEA, 0xF3);  // borudaki akis cizgileri
    private static Color PipeSheen = C(0xFF, 0xFF, 0xFF); // boru parlak sheen
    private static Color TipBg = C(0x1B, 0x28, 0x38);     // ipucu kutusu zemin
    private static Color TipBorder = C(0x36, 0x45, 0x55); // ipucu kenar
    private static byte SheenA = 70;                      // sheen opakligi (temaya gore)
    private static Color GeoSeaCol = C(0x5B, 0x76, 0x8E); // deniz etiketi
    private static Color GeoCountryCol = C(0x6C, 0x7C, 0x8C); // ulke etiketi
    private static Color SepPenCol = C(0xFF, 0xE3, 0xE3); // dilim ayırıcıları

    private static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    /// <summary>Harita paletini koyu/açık tema arasında değiştirir (ThemeManager çağırır).</summary>
    public static void ApplyTheme(bool light)
    {
        if (light)
        {
            Bg = C(0xEE, 0xF2, 0xF6); Sea = C(0xB6, 0xD2, 0xE8); Land = C(0xE8, 0xEE, 0xF4);
            NeighLand = C(0xDB, 0xE3, 0xEC); NeighEdge = C(0x9D, 0xB0, 0xC2); Province = C(0xC2, 0xCF, 0xDC);
            TextCol = C(0x16, 0x20, 0x2B); MutedCol = C(0x5B, 0x6B, 0x7C);
            FlowDash = C(0x2A, 0x3C, 0x4E); PipeSheen = C(0xFF, 0xFF, 0xFF); SheenA = 40;
            TipBg = C(0xFF, 0xFF, 0xFF); TipBorder = C(0xC2, 0xCF, 0xDC);
            GeoSeaCol = C(0x5E, 0x7B, 0x97); GeoCountryCol = C(0x7B, 0x8B, 0x9C);
            SepPenCol = C(0xFF, 0xE3, 0xE3);

        }
        else
        {
            Bg = C(0x0F, 0x16, 0x20); Sea = C(0x0D, 0x24, 0x3A); Land = C(0x1B, 0x27, 0x33);
            NeighLand = C(0x14, 0x1D, 0x29); NeighEdge = C(0x3C, 0x4E, 0x60); Province = C(0x36, 0x45, 0x55);
            TextCol = C(0xE6, 0xEE, 0xF6); MutedCol = C(0x9A, 0xAF, 0xC4);
            FlowDash = C(0xDF, 0xEA, 0xF3); PipeSheen = C(0xFF, 0xFF, 0xFF); SheenA = 70;
            TipBg = C(0x1B, 0x28, 0x38); TipBorder = C(0x36, 0x45, 0x55);
            GeoSeaCol = C(0x5B, 0x76, 0x8E); GeoCountryCol = C(0x6C, 0x7C, 0x8C);
            SepPenCol = C(0xFF, 0xE3, 0xE3);
        }
    }

    private IPipelineSource? _source;
    public IPipelineSource? Source
    {
        get => _source;
        set { _source = value; InvalidateVisual(); }
    }

    public event Action<PNode>? NodeClicked;

    /// <summary>
    /// Gizlenen dugum kategorileri: "CS", "BORDER", "OFFTAKE", "STORAGE",
    /// "OTHER" (kavsak/LNG/FSRU/terminal). Alt bar onay kutulari doldurur.
    /// Segmentler cizilmeye devam eder; yalnizca daire + etiket gizlenir.
    /// </summary>
    public HashSet<string> HiddenCategories { get; } = new();

    // --- Katman gorunurlugu (ozellestirme; "Katmanlar" menusu doldurur) ---
    private bool _showProvinces = true, _showGeoLabels = true, _showFlow = true, _showGrid = false;
    public bool ShowProvinceBorders { get => _showProvinces; set { _showProvinces = value; InvalidateVisual(); } }
    public bool ShowGeoLabels { get => _showGeoLabels; set { _showGeoLabels = value; InvalidateVisual(); } }
    public bool ShowFlowArrows { get => _showFlow; set { _showFlow = value; InvalidateVisual(); } }
    // Enlem/boylam ızgarası (koordinat izgarasi); varsayilan kapali.
    public bool ShowGrid { get => _showGrid; set { _showGrid = value; InvalidateVisual(); } }

    // --- Saglik gorunumu (ust bar "Saglik gorunumu" menusu doldurur) ---
    // Daire dolgusu: pasta (unite basina dilim) / ortalama / en kotu. Alarm HER
    // ZAMAN en kotu uniteye + CriticalThreshold esigine bakar (moddan bagimsiz).
    private HealthDisplayMode _healthMode = HealthDisplayMode.Pie;
    public HealthDisplayMode HealthMode { get => _healthMode; set { _healthMode = value; InvalidateVisual(); } }
    private HealthAggregate _outlineAgg = HealthAggregate.Worst;
    public HealthAggregate OutlineAggregate { get => _outlineAgg; set { _outlineAgg = value; InvalidateVisual(); } }
    private double _criticalThreshold = 20;
    public double CriticalThreshold { get => _criticalThreshold; set { _criticalThreshold = value; InvalidateVisual(); } }

    // Kullanıcı seçimi (Sağlık Görünümü renk seçici); null → SepPenCol (tema varsayılanı).
    private Color? _pieSepCustom;
    public Color EffectivePieSeparatorColor => _pieSepCustom ?? SepPenCol;

    public void SetPieSeparatorColorHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) _pieSepCustom = null;
        else if (ColorUtil.TryParseHex(hex, out var c)) _pieSepCustom = c;
        InvalidateVisual();
    }

    // Komsu ulke / deniz / onemli sehir etiketleri (cografi baglam).
    private enum GeoKind { Sea, Country, City }
    private readonly record struct GeoLabel(string Name, double Lat, double Lon, GeoKind Kind);
    private static readonly GeoLabel[] GeoLabels =
    {
        new("K A R A D E N İ Z", 43.3, 34.5, GeoKind.Sea),
        new("A K D E N İ Z", 35.0, 31.5, GeoKind.Sea),
        new("E G E\nD E N İ Z İ", 38.4, 24.9, GeoKind.Sea),
        new("MARMARA D.", 40.6, 27.9, GeoKind.Sea),
        new("BULGARİSTAN", 42.3, 25.2, GeoKind.Country),
        new("YUNANİSTAN", 39.7, 22.2, GeoKind.Country),
        new("GÜRCİSTAN", 42.4, 43.4, GeoKind.Country),
        new("ERMENİSTAN", 40.2, 45.4, GeoKind.Country),
        new("İRAN", 37.8, 45.9, GeoKind.Country),
        new("IRAK", 35.4, 43.9, GeoKind.Country),
        new("SURİYE", 35.1, 38.6, GeoKind.Country),
        new("RUSYA", 43.5, 37.8, GeoKind.Country),
    };

    // Adi haritada varsayilan olarak gorunen onemli sehirler (istasyon dugumleri).
    private static readonly string[] MajorCities =
        { "İstanbul", "Ankara", "İzmir", "Bursa", "Adana", "Gaziantep", "Konya", "Ceyhan" };
    private static bool IsMajorCity(PNode n) =>
        n.IsStation && Array.Exists(MajorCities, c => n.Name.StartsWith(c, StringComparison.Ordinal));

    private bool IsHidden(PNode n) => HiddenCategories.Contains(CategoryOf(n));

    private static string CategoryOf(PNode n) =>
        n.IsStation || n.Type == "CS" ? "CS"
        : n.Type is "BORDER" or "OFFTAKE" or "STORAGE" ? n.Type
        : n.Type is "PS" or "PT" or "OILDEPO" ? "OIL" // petrol pompa / depo
        : "OTHER";

    // Tiklaninca detay paneli acan (menusu olan) dugumler: gaz kompresor istasyonu,
    // ham petrol pompa/terminal, gaz/petrol deposu. Digerleri (sinir/cikis/kavsak/
    // uniteli olmayan CS vb.) menu acmaz -> etkilesimli degil: hover imleci, tiklama
    // ve cokusme (cluster) balonunda gosterilmez. Kural MapWindow.OnNodeClicked ile ayni.
    internal static bool IsInteractive(PNode n) =>
        n.IsStation || n.Type is "PS" or "PT" or "STORAGE" or "OILDEPO";

    private readonly Dictionary<string, Point> _screen = new();
    // Segment ekran noktalari (polyline guzergah; hover mesafesi icin cache).
    private readonly Dictionary<string, Point[]> _segScreen = new();

    private string? _hoverId;   // uzerine gelinen dugum/segment
    private bool _hoverSeg;
    private int _hoverStartTick; // hover buyume animasyonu baslangici

    // Ust uste binen dugumler icin secim balonu (hover ile acilir).
    private List<PNode>? _cluster;          // ayni noktadaki >1 dugum
    private Point _clusterAnchor;           // balonun baglandigi ekran noktasi
    private Rect _clusterBox;               // balon dikdortgeni (hit-test icin)
    private double _clusterHeaderH, _clusterRowH;
    private int _clusterHover = -1;         // fare altindaki satir (-1 yok)
    private Point _mouse;

    // Secili istasyon: panel <-> harita baglantisi icin parlak halka cizilir.
    private string? _selectedId;
    public string? SelectedId
    {
        get => _selectedId;
        set { _selectedId = value; InvalidateVisual(); }
    }

    // Provins + deniz geometrisi cache (boyut/zoom/pan degisince yeniden kurulur).
    private Geometry? _provGeo;
    private Geometry? _seaGeo;
    private double _pkW, _pkH, _pkZoom = 1, _pkPanX, _pkPanY;
    // Projeksiyon sinirlari cache (dugum kumesi degisince — kaynak/canli feed — yeniden kurulur).
    private double _pkMinLat, _pkMaxLat, _pkMinLon, _pkMaxLon;

    // Deniz alanlari (lat/lon [lon,lat] halkalari): kara zemininden "su" oyar.
    // Kara (Turkiye) tarafi kenarlar bilerek kiyinin icine tasar ki deniz ile
    // kara arasinda bosluk kalmasin; Turkiye ustte cizildiginden fazlalik kapanir.
    // Denizler birbiriyle serbestce ortusur (BuildRoundedRingGeometry Nonzero ile birlestirir).
    private static readonly double[][][] SeaPolys =
    {
        new[]{ new[]{26.3,40.0}, new[]{43.4,40.0}, new[]{43.4,50.0}, new[]{26.3,50.0} }, // Karadeniz
        new[]{ new[]{23.2,32.5}, new[]{28.7,32.5}, new[]{28.7,41.3}, new[]{23.2,41.3} }, // Ege
        new[]{ new[]{25.8,23.0}, new[]{37.8,23.0}, new[]{37.8,37.8}, new[]{25.8,37.8} }, // Akdeniz
        new[]{ new[]{25.8,39.6}, new[]{30.4,39.6}, new[]{30.4,41.9}, new[]{25.8,41.9} }, // Marmara
    };

    // Turkiye sinirindan disari giden komsu sinir cizgileri (lat/lon [lon,lat] ciftleri).
    private static readonly double[][][] DividerLines =
    {
        new[]{ new[]{26.6,41.9}, new[]{24.6,43.8} }, // Bulgaristan | Yunanistan (KB)
        new[]{ new[]{43.5,41.2}, new[]{45.8,42.7} }, // Gurcistan | Ermenistan (KD)
        new[]{ new[]{44.6,39.5}, new[]{47.4,39.0} }, // Ermenistan | Iran (D)
        new[]{ new[]{44.4,37.6}, new[]{47.2,36.2} }, // Iran | Irak (GD)
        new[]{ new[]{42.3,37.1}, new[]{41.2,34.2} }, // Irak | Suriye (G)
        new[]{ new[]{36.6,36.5}, new[]{36.9,33.8} }, // Suriye guney (G)
    };

    // Taban projeksiyonu (zoom/pan'siz); bolge odaklama hesabi icin saklanir.
    private Func<double, double, Point>? _geoProj;
    private bool _geoReady;
    private Point GeoAt(double lat, double lon) => _geoProj!(lat, lon);

    // Zoom + pan durumu.
    private double _zoom = 1, _panX, _panY;
    // OnRender'da hesaplanir: projeksiyon menzili genisletilmis olsa bile Turkiye'yi
    // eski tam-gorunum boyutunda cerceveleyen zoom (cift tik / "Tumu" bunu kullanir).
    private double _fitZoom = 1;
    // Ilk gercek render'da bir kez Turkiye'ye cerceveler (varsayilan gorunum) — boylece
    // acilis, genisletilmis menzilin tamamini degil Turkiye'yi gosterir.
    private bool _needsInitialFit = true;
    private bool _dragging, _moved;
    private Point _dragStart;
    private double _panStartX, _panStartY;

    // Il sinirlarinin cografi kutusu (bir kez hesaplanir).
    private static bool _bboxReady;
    private static double _minLon, _maxLon, _minLat, _maxLat;

    static PipelineMapControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(PipelineMapControl), new FrameworkPropertyMetadata(typeof(PipelineMapControl)));
    }

    private static void EnsureBbox()
    {
        if (_bboxReady) return;
        _bboxReady = true;
        _minLon = _minLat = 1e9; _maxLon = _maxLat = -1e9;
        foreach (var p in TurkeyMap.Provinces)
            foreach (var ring in p.Rings)
                foreach (var pt in ring)
                {
                    if (pt[0] < _minLon) _minLon = pt[0]; if (pt[0] > _maxLon) _maxLon = pt[0];
                    if (pt[1] < _minLat) _minLat = pt[1]; if (pt[1] > _maxLat) _maxLat = pt[1];
                }
    }

    private static Color HealthColor(double h) =>
        h >= 70 ? Color.FromRgb(0x2E, 0xCC, 0x71)
      : h >= 40 ? Color.FromRgb(0xF1, 0xC4, 0x0F)
      : h >= 20 ? Color.FromRgb(0xE6, 0x7E, 0x22)
      : Color.FromRgb(0xE7, 0x4C, 0x3C);

    private static Brush HealthBrush(double h) => new SolidColorBrush(HealthColor(h));

    // Telemetrisi olmayan unite dilimi (gri "bilinmeyen").
    private static readonly Color UnknownCol = Color.FromRgb(0x5B, 0x6B, 0x7C);

    // btaş.jpg sınıflandırması: petrol zeytin-yeşili; depo/yükleme sarımsı (altıgen);
    // TANAP mor; TürkAkım mavi.
    private static readonly Color OilCol = Color.FromRgb(0x9A, 0xBE, 0x3A);        // petrol boru + pompa
    private static readonly Color OilDepoCol = Color.FromRgb(0xC8, 0xD1, 0x2E);    // petrol depo/yükleme
    private static readonly Color TanapCol = Color.FromRgb(0xA6, 0x5E, 0xE0);      // TANAP gaz hattı
    private static readonly Color TurkStreamCol = Color.FromRgb(0x3A, 0xA0, 0xE0); // TürkAkım gaz hattı

    private static Color NodeTypeColor(string type) => type switch
    {
        "BORDER" => Color.FromRgb(0x4E, 0x9B, 0xF5),
        "OFFTAKE" => Color.FromRgb(0xC5, 0x8A, 0xF5),
        "STORAGE" => Color.FromRgb(0x4E, 0xCD, 0xC4),
        "PS" or "PT" => OilCol,              // petrol pompa istasyonu (kare)
        "OILDEPO" => OilDepoCol,             // petrol depolama/yükleme (altıgen)
        _ => Color.FromRgb(0x8C, 0xA3, 0xB8),
    };

    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        // Zemin = kara (Turkiye disi her yer); denizler ustte oyulur.
        dc.DrawRectangle(new SolidColorBrush(NeighLand), null, new Rect(0, 0, w, h));
        if (_source == null || w < 40 || h < 40) return;

        EnsureBbox();
        var nodes = PipelineTopology.Nodes;
        bool hasMap = TurkeyMap.Provinces.Count > 0 && _maxLon > _minLon;

        // Projeksiyon sinirlari: Turkiye il kutusu + canli feed'den gelen TUM makul dugum
        // koordinatlari. Boylece menzil disi gelen dugumler (or. Mavi Akim / Rusya sinir
        // girisi ~48°K) kirpilmadan gercek goreli konumlarinda cizilir; harita gerektigi
        // kadar genis alani kapsar. Sacma/eksik (0,0) ya da bolge disi koordinatlar tum
        // haritayi bozmasin diye genis bir bolgesel bant (25–55°K, 15–60°D) ile elenir.
        double pMinLat = _minLat, pMaxLat = _maxLat, pMinLon = _minLon, pMaxLon = _maxLon;
        if (hasMap)
            foreach (var n in nodes)
            {
                if (n.Lat is < 25 or > 55 || n.Lon is < 15 or > 60) continue; // bolge disi: yok say
                if (n.Lat < pMinLat) pMinLat = n.Lat; if (n.Lat > pMaxLat) pMaxLat = n.Lat;
                if (n.Lon < pMinLon) pMinLon = n.Lon; if (n.Lon > pMaxLon) pMaxLon = n.Lon;
            }

        // Ikon boyutu duzeltmesi: menzil genisleyip harita kuculdugunde (or. ~48°K dugum
        // sinirlari 2 kat uzatir) sabit-px ikonlar orantisiz BUYUK gorunur. Ikonlari
        // harita olcegiyle orantili kucultup eski goreli boyutu korur. Genisleme yoksa
        // (Turkiye tam kaplar) oran = 1 → ikonlar aynen eskisi gibi.
        double iconScaleAdj = 1;

        Func<double, double, Point> geoProj;
        if (hasMap)
        {
            // En-boy düzeltmeli projeksiyon: boylam dereceleri enlem cos'u kadar kısalır.
            double midLat = (pMinLat + pMaxLat) / 2.0;
            double k = Math.Cos(midLat * Math.PI / 180.0);
            double scaledW = (pMaxLon - pMinLon) * k;
            double scaledH = (pMaxLat - pMinLat);
            const double pad = 78; // komsu deniz/ulke etiketlerine yer birak
            double scale = Math.Min((w - 2 * pad) / scaledW, (h - 2 * pad) / scaledH);
            double ox = (w - scaledW * scale) / 2.0;
            double oy = (h - scaledH * scale) / 2.0;
            geoProj = (lat, lon) => new Point(ox + (lon - pMinLon) * k * scale, oy + (pMaxLat - lat) * scale);

            // Turkiye taban olcegi (genisletilmemis): ikon oranini buna gore normalize et.
            double refK = Math.Cos((_minLat + _maxLat) / 2.0 * Math.PI / 180.0);
            double refScale = Math.Min((w - 2 * pad) / ((_maxLon - _minLon) * refK),
                                       (h - 2 * pad) / (_maxLat - _minLat));
            iconScaleAdj = Math.Clamp(scale / refScale, 0.45, 1.0);
            // Turkiye'yi taban (genisletilmemis) olcegine getiren zoom: cift tik odagi.
            _fitZoom = Math.Clamp(refScale / scale, 0.45, 8.0);

            // Varsayilan gorunum: acilista bir kez Turkiye'ye cerceve (menzil kuzeye
            // genisletilmis olsa bile). Cizimden ONCE zoom/pan set edilir → tek karede,
            // titremeden. Bundan sonra gorunumu kullanici (cift tik/teker/pan) yonetir.
            if (_needsInitialFit)
            {
                var cFit = geoProj((_minLat + _maxLat) / 2, (_minLon + _maxLon) / 2);
                _zoom = _fitZoom;
                _panX = w / 2 - cFit.X * _zoom;
                _panY = h / 2 - cFit.Y * _zoom;
                _needsInitialFit = false;
            }
        }
        else
        {
            double minLat = nodes.Min(n => n.Lat), maxLat = nodes.Max(n => n.Lat);
            double minLon = nodes.Min(n => n.Lon), maxLon = nodes.Max(n => n.Lon);
            const double pad = 70;
            geoProj = (lat, lon) => new Point(
                pad + (lon - minLon) / Math.Max(1e-6, maxLon - minLon) * (w - 2 * pad),
                pad + (maxLat - lat) / Math.Max(1e-6, maxLat - minLat) * (h - 2 * pad));
        }

        _geoProj = geoProj; _geoReady = true; // bolge odaklama icin sakla

        // Zoom + pan (etiketler sabit boyutta kalır, konumlar dönüşür).
        Func<double, double, Point> project = (lat, lon) =>
        {
            var b = geoProj(lat, lon);
            return new Point(b.X * _zoom + _panX, b.Y * _zoom + _panY);
        };

        if (hasMap)
        {
            if (_provGeo == null || _pkW != w || _pkH != h
                || _pkZoom != _zoom || _pkPanX != _panX || _pkPanY != _panY
                || _pkMinLat != pMinLat || _pkMaxLat != pMaxLat
                || _pkMinLon != pMinLon || _pkMaxLon != pMaxLon)
            {
                _provGeo = BuildRingGeometry(TurkeyMap.Provinces.SelectMany(p => p.Rings), project);
                // Deniz bloklari: kose yuvarlatmayla yumusatilir (dikdortgen his kalkar);
                // Turkiye sinirlari keskin kalir -> dusuk detayli kiyi korunur.
                _seaGeo = BuildRoundedRingGeometry(SeaPolys, project, 22 * _zoom);
                _pkW = w; _pkH = h; _pkZoom = _zoom; _pkPanX = _panX; _pkPanY = _panY;
                _pkMinLat = pMinLat; _pkMaxLat = pMaxLat; _pkMinLon = pMinLon; _pkMaxLon = pMaxLon;
            }

            // Denizler: kara zemininden su (mavi) oyar -> kiyilar belirir.
            // Once yumusak "feather": genisleyen soluk deniz halkalari, deniz ile komsu
            // karayi sert kenar yerine gradyan gecisle kaynastirir (kaynasik gorunum);
            // sonra tam dolgu. Turkiye ustte cizildiginden kendi kiyisi keskin kalir,
            // komsu-tarafi duz sinir cizgileri (DividerLines) sonra ciziip korunur.
            if (_seaGeo != null)
            {
                for (int i = 6; i >= 1; i--)
                {
                    var feather = new Pen(new SolidColorBrush(Color.FromArgb((byte)(48 - i * 6), Sea.R, Sea.G, Sea.B)),
                                          i * 9 * Math.Clamp(_zoom, 0.75, 1.8))
                        { LineJoin = PenLineJoin.Round };
                    dc.DrawGeometry(null, feather, _seaGeo);
                }
                dc.DrawGeometry(new SolidColorBrush(Sea), null, _seaGeo);
            }

            // Komsu sinir cizgileri: Turkiye'den disari giden duz cizgiler.
            if (_showProvinces)
            {
                var divPen = new Pen(new SolidColorBrush(NeighEdge), 1.1);
                foreach (var d in DividerLines)
                    dc.DrawLine(divPen, project(d[0][1], d[0][0]), project(d[1][1], d[1][0]));
            }

            // Turkiye (daha acik) ustte -> belirgin durur, denizin fazlasini kapatir.
            dc.DrawGeometry(new SolidColorBrush(Land),
                            _showProvinces ? new Pen(new SolidColorBrush(Province), 0.7) : null, _provGeo);
        }

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (_showGrid) DrawGraticule(dc, project, dpi);
        if (_showGeoLabels) DrawGeoLabels(dc, project, dpi);
        DrawRegionLabels(dc, project, dpi);

        // Dugum konumu: projeksiyon sinirlari yukarida canli feed'deki tum makul
        // dugumleri kapsayacak sekilde genisletildiginden, bunlar zaten harita icine
        // duser. Bu sikistirma yalnizca bir EMNIYET agi: bolgesel bant disinda elenen
        // (or. 0,0 / bozuk) koordinatlar yine de gorunur kenara cekilir, kaybolmaz.
        // Sikistirma TABAN projeksiyonunda (zoom/pan oncesi): harita/segment geometrisi
        // bozulmaz, yakinlasip kaydirinca dugum normal hareket eder.
        const double edgeInset = 10;
        _screen.Clear();
        foreach (var n in nodes)
        {
            var b = geoProj(n.Lat, n.Lon); // taban (zoom/pan'siz) nokta
            b.X = Math.Clamp(b.X, edgeInset, w - edgeInset);
            b.Y = Math.Clamp(b.Y, edgeInset, h - edgeInset);
            _screen[n.Id] = new Point(b.X * _zoom + _panX, b.Y * _zoom + _panY);
        }

        double phase = (Environment.TickCount % 2000) / 2000.0;
        var lowCol = Color.FromRgb(0x4E, 0xCD, 0xC4);
        var highCol = Color.FromRgb(0xFF, 0x6B, 0x6B);

        // --- Segmentler ---
        _segScreen.Clear();
        foreach (var seg in PipelineTopology.Segments)
        {
            // Ucu bilinmeyen segmenti atla (bozuk snapshot verisine karsi).
            if (!_screen.TryGetValue(seg.From, out var pFrom)
                || !_screen.TryGetValue(seg.To, out var pTo)) continue;

            // Guzergah: Geometry doluysa gercek polyline, yoksa duz cizgi.
            Point[] pts;
            if (seg.Geometry is { Count: >= 2 } geo)
            {
                pts = new Point[geo.Count];
                for (int i = 0; i < geo.Count; i++)
                    pts[i] = project(geo[i].Lat, geo[i].Lon);
            }
            else pts = new[] { pFrom, pTo };
            _segScreen[seg.Id] = pts;

            var snap = _source.Segment(seg.Id);
            // Hat türüne göre renk (btaş.jpg): petrol zeytin, TANAP mor, TürkAkım mavi;
            // diğer gaz akış rengiyle (turkuaz→kırmızı, doluluğa göre).
            (Color col, bool typed) = seg.Product switch
            {
                "oil" => (OilCol, true),
                "gas_tanap" => (TanapCol, true),
                "gas_turkstream" => (TurkStreamCol, true),
                _ => (Lerp(lowCol, highCol, snap.LoadRatio), false),
            };
            double thick = typed ? 3.8 : 2.5 + snap.LoadRatio * 4.0;

            // Boru gorunumu: dista koyu kilif, ustte ana renk, ortada ince parlak
            // sheen -> silindirik "boru" hissi. Yuvarlak uclar/birlesimler purussuz.
            var casingCol = Color.FromArgb(150, (byte)(col.R * 0.35), (byte)(col.G * 0.35), (byte)(col.B * 0.35));
            var casingPen = new Pen(new SolidColorBrush(casingCol), thick + 2.5)
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var mainPen = new Pen(new SolidColorBrush(col), thick)
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var sheenPen = new Pen(new SolidColorBrush(Color.FromArgb(SheenA, PipeSheen.R, PipeSheen.G, PipeSheen.B)), Math.Max(0.8, thick * 0.32))
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            for (int i = 0; i + 1 < pts.Length; i++) dc.DrawLine(casingPen, pts[i], pts[i + 1]);
            for (int i = 0; i + 1 < pts.Length; i++) dc.DrawLine(mainPen, pts[i], pts[i + 1]);
            for (int i = 0; i + 1 < pts.Length; i++) dc.DrawLine(sheenPen, pts[i], pts[i + 1]);

            // Sizinti: yanip sonen kirmizi kalin katman.
            if (snap.Leak)
            {
                double lp = 0.5 + 0.5 * Math.Sin(Environment.TickCount / 200.0);
                byte la = (byte)(60 + lp * 190);
                var leakPen = new Pen(new SolidColorBrush(Color.FromArgb(la, 0xE7, 0x4C, 0x3C)), thick + 7)
                    { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                for (int i = 0; i + 1 < pts.Length; i++) dc.DrawLine(leakPen, pts[i], pts[i + 1]);
            }

            // Toplam guzergah uzunlugu (akis noktalari + etiket konumu icin).
            double segLen = 0;
            for (int i = 0; i + 1 < pts.Length; i++) segLen += (pts[i + 1] - pts[i]).Length;

            // Akis: borunun icinde akan soluk kisa cizgiler (yon boyunca kayar).
            // Eski parlak beyaz oklardan daha ince ve dikkat dagitmayan.
            if (_showFlow && segLen > 20)
            {
                int dashN = Math.Clamp((int)(segLen / 40), 2, 10);
                double dashLen = Math.Min(6, thick * 1.6);
                var flowPen = new Pen(new SolidColorBrush(Color.FromArgb(120, FlowDash.R, FlowDash.G, FlowDash.B)),
                    Math.Max(1.0, thick * 0.5)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                for (int i = 0; i < dashN; i++)
                {
                    double t = (phase + (double)i / dashN) % 1.0;
                    double d = segLen * t;
                    var a = PointAlong(pts, Math.Max(0, d - dashLen / 2), out _);
                    var b = PointAlong(pts, Math.Min(segLen, d + dashLen / 2), out _);
                    dc.DrawLine(flowPen, a, b);
                }
            }

            // Etiket: guzergahin orta noktasi, yerel yone dik yukari kaydirilir.
            var mid = PointAlong(pts, segLen / 2, out var dir);
            var perp = new Vector(-dir.Y, dir.X); if (perp.Y > 0) perp.Negate(); // etiket yukari tarafa
            var lc = new Point(mid.X + perp.X * 16, mid.Y + perp.Y * 16);
            // Akis etiketi yalniz yakinlasinca ya da uzerine gelince (kalabaligi onle).
            bool showFlow = (_zoom > 2.2 || _hoverId == seg.Id) && segLen > 90;
            if (showFlow)
            {
                var ft = Text($"{snap.FlowMcmDay:0} mcm/g", 10.5, MutedCol, dpi);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(150, Bg.R, Bg.G, Bg.B)), null,
                    new Rect(lc.X - ft.Width / 2 - 3, lc.Y - ft.Height / 2 - 1, ft.Width + 6, ft.Height + 2));
                dc.DrawText(ft, new Point(lc.X - ft.Width / 2, lc.Y - ft.Height / 2));
            }

            if (snap.Leak)
            {
                var lc2 = new Point(mid.X - perp.X * 16, mid.Y - perp.Y * 16); // diger tarafa
                var lft = Text("⚠ SIZINTI", 11, Color.FromRgb(0xE7, 0x4C, 0x3C), dpi);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(170, Bg.R, Bg.G, Bg.B)), null,
                    new Rect(lc2.X - lft.Width / 2 - 3, lc2.Y - lft.Height / 2 - 1, lft.Width + 6, lft.Height + 2));
                dc.DrawText(lft, new Point(lc2.X - lft.Width / 2, lc2.Y - lft.Height / 2));
            }
        }

        // --- Düğümler ---
        // Daire boyutu zoom'a bagli: tam gorunumde kucuk, yakinlastikca buyur
        // (yakin sehir dugumleri ust uste binmesin).
        double nodeScale = Math.Clamp(0.55 + 0.42 * (_zoom - 1), 0.4, 1.7) * iconScaleAdj;

        // Etiket kalabaligini onle: varsayilan olarak yalniz secili/hover; yeterince
        // yakinlasinca istasyon adlari da gorunur.
        bool zoomLabels = _zoom > 2.2;

        var labelDraws = new List<Action>(); // etiketler tum dairelerden SONRA (ustte kalir)
        foreach (var n in nodes)
        {
            if (IsHidden(n)) continue; // kategori gizli: daire + etiket cizilmez
            var p = _screen[n.Id];
            var snap = _source.Node(n.Id);
            bool hovered = n.Id == _hoverId && !_hoverSeg;
            bool dimmed = _activeRegion != null && n.Region != _activeRegion; // izolasyon
            double r = (n.IsStation ? 12 : 8) * nodeScale;

            // Hover: 120ms'de yumusak %20 buyume.
            if (hovered)
            {
                double ht = Math.Clamp((Environment.TickCount - _hoverStartTick) / 120.0, 0, 1);
                r *= 1 + 0.20 * (ht * ht * (3 - 2 * ht)); // smoothstep
            }

            if (dimmed) dc.PushOpacity(0.22);
            DrawNodeGlyph(dc, n, p, r, snap, nodeScale);
            if (dimmed) dc.Pop();

            // Secili istasyon: parlak halka — detay paneliyle gorsel bag.
            if (n.Id == _selectedId)
            {
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(60, TextCol.R, TextCol.G, TextCol.B)), 5), p, r + 7, r + 7);
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(235, TextCol.R, TextCol.G, TextCol.B)), 1.6), p, r + 5, r + 5);
            }

            // Etiket yalniz: secili, hover, onemli sehir, ya da yakinlasinca (istasyonlar).
            bool showLabel = !dimmed && (n.Id == _selectedId || hovered || IsMajorCity(n) || (zoomLabels && n.IsStation));
            if (showLabel)
            {
                var nc = n; var pc = p; var rc = r;
                labelDraws.Add(() =>
                {
                    var name = Text(nc.Name, 11.5, TextCol, dpi);
                    DrawLabel(dc, name, new Point(pc.X - name.Width / 2, pc.Y + rc + 5));
                });
            }
        }

        // Etiket gecisi: hicbir daire etiketin ustune binmez.
        foreach (var draw in labelDraws) draw();

        DrawTooltip(dc, dpi);
        DrawClusterChooser(dc, dpi);
    }

    // Dugum turune gore ayri gorsel: istasyon=daire, depo=silindir, sinir=elmas,
    // cikis=asagi ucgen, kavsak/diger=ici bos halka. Boylece tur bir bakista okunur.
    private void DrawNodeGlyph(DrawingContext dc, PNode n, Point p, double r, NodeSnap snap, double nodeScale)
    {
        var bgPen = new Pen(new SolidColorBrush(Bg), Math.Max(1, 2 * nodeScale));

        if (n.IsStation)
        {
            var units = _source?.UnitHealths(n.Id) ?? Array.Empty<UnitHealth>();
            double worst = snap.Health; // Node() = en kotu (min); alarm hep buna bakar
            // Ozet (kenar/dis hare) rengi: kullanicinin sectigi toplama kuralina gore.
            Color summaryC = HealthColor(HealthAgg.Combine(units, _outlineAgg, snap.Health));

            if (worst < _criticalThreshold) // kritik: genis isi haresi + yanip sonen halka
            {
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(24, 0xE7, 0x4C, 0x3C)), null, p, r + 26, r + 26);
                double pulse = 0.5 + 0.5 * Math.Sin(Environment.TickCount / 300.0);
                byte a = (byte)(40 + pulse * 190);
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(a, 0xE7, 0x4C, 0x3C)), 3), p, r + 10, r + 10);
            }
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(70, summaryC.R, summaryC.G, summaryC.B)), null, p, r + 6, r + 6);

            if (_healthMode == HealthDisplayMode.Pie && units.Count > 1)
            {
                DrawHealthPie(dc, p, r, units, summaryC, nodeScale);
            }
            else
            {
                double fillH; bool gray = false;
                if (_healthMode == HealthDisplayMode.Average)
                    fillH = HealthAgg.Combine(units, HealthAggregate.Average, snap.Health);
                else if (_healthMode == HealthDisplayMode.Pie && units.Count == 1)
                { fillH = units[0].Health; gray = !units[0].HasTelemetry; }
                else
                    fillH = worst; // En Kotu modu (ya da unitesiz kaynak → en kotu)
                Brush fill = gray ? new SolidColorBrush(UnknownCol) : HealthBrush(fillH);
                dc.DrawEllipse(fill, bgPen, p, r, r);
            }
            return;
        }

        var col = NodeTypeColor(n.Type);
        var colBrush = new SolidColorBrush(col);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(55, col.R, col.G, col.B)), null, p, r + 5, r + 5);

        switch (n.Type)
        {
            case "STORAGE":
            case "OILDEPO":
            {
                // Silindir (depo): govde + doluluk + ust elips. Hem gaz UGS hem petrol
                // deposu (OILDEPO) canli doluluk yuzdesini (s_storage_level_pct) gosterir.
                // Ikon aynidir; renk turden gelir (UGS turkuaz, petrol deposu sarimsi).
                double rw = r * 1.3, rh = r * 1.7, ry = r * 0.42;
                var body = new Rect(p.X - rw, p.Y - rh + ry, rw * 2, rh * 2 - ry * 2);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(120, Bg.R, Bg.G, Bg.B)), null, body);
                double lvl = Math.Clamp(_source?.Level(n.Id) ?? 0, 0, 100);
                double fh = body.Height * lvl / 100.0;
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, col.R, col.G, col.B)), null,
                    new Rect(body.X, body.Bottom - fh, body.Width, fh));
                var pen = new Pen(colBrush, Math.Max(1, 1.6 * nodeScale));
                dc.DrawRectangle(null, pen, body);
                dc.DrawEllipse(new SolidColorBrush(col), pen, new Point(p.X, body.Top), rw, ry);
                break;
            }
            case "BORDER":
            {
                // Elmas (sinir gecisi).
                double d = r * 1.25;
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(p.X, p.Y - d), true, true);
                    ctx.LineTo(new Point(p.X + d, p.Y), true, false);
                    ctx.LineTo(new Point(p.X, p.Y + d), true, false);
                    ctx.LineTo(new Point(p.X - d, p.Y), true, false);
                }
                g.Freeze();
                dc.DrawGeometry(colBrush, bgPen, g);
                break;
            }
            case "OFFTAKE":
            {
                // Asagi ucgen (sehir cikisi: gaz disari).
                double d = r * 1.15;
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(p.X - d, p.Y - d * 0.7), true, true);
                    ctx.LineTo(new Point(p.X + d, p.Y - d * 0.7), true, false);
                    ctx.LineTo(new Point(p.X, p.Y + d), true, false);
                }
                g.Freeze();
                dc.DrawGeometry(colBrush, bgPen, g);
                break;
            }
            case "PS":
            case "PT":
            {
                // Petrol pompa istasyonu: yesil kare "kalkan".
                double d = r * 1.05;
                dc.DrawRoundedRectangle(colBrush, bgPen,
                    new Rect(p.X - d, p.Y - d, d * 2, d * 2), d * 0.3, d * 0.3);
                break;
            }
            default: // JUNCTION / LNG / FSRU / TERMINAL: ici bos halka
                dc.DrawEllipse(new SolidColorBrush(Bg), new Pen(colBrush, Math.Max(1.5, 2.4 * nodeScale)), p, r, r);
                break;
        }
    }

    // Istasyon dairesini unite basina esit dilimlere boler; her dilim o unitenin
    // sagligiyla boyanir (telemetrisi yoksa gri). Ustte ozet renginde belirgin
    // kenar halkasi -> uzaklasinca bile bir bakista okunur.
    private void DrawHealthPie(DrawingContext dc, Point center, double r,
        IReadOnlyList<UnitHealth> units, Color summaryC, double nodeScale)
    {
        int n = units.Count;
        // Dilim ayırıcıları: siyah, %5 opaklık (alfa 0x0D) — çok soluk ayırıcı çizgi.
        var sepPen = new Pen(new SolidColorBrush(Color.FromArgb(0x0D, 0x00, 0x00, 0x00)), Math.Max(0.6, 0.9 * nodeScale));
        for (int i = 0; i < n; i++)
        {
            double a0 = -90 + 360.0 * i / n;
            double a1 = -90 + 360.0 * (i + 1) / n;
            Color col = units[i].HasTelemetry ? HealthColor(units[i].Health) : UnknownCol;
            dc.DrawGeometry(new SolidColorBrush(col), sepPen, WedgeGeometry(center, r, a0, a1));
        }
        // Ozet kenar: okunurluk icin belirgin halka (en kotu ya da ortalama rengi).
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(summaryC), Math.Max(1.6, 2.4 * nodeScale)), center, r, r);
    }

    // Merkezden a0->a1 (derece) arasi pasta dilimi.
    private static Geometry WedgeGeometry(Point c, double r, double a0deg, double a1deg)
    {
        double a0 = a0deg * Math.PI / 180, a1 = a1deg * Math.PI / 180;
        var p0 = new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0));
        var p1 = new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(c, true, true);
            ctx.LineTo(p0, true, false);
            ctx.ArcTo(p1, new Size(r, r), 0, (a1deg - a0deg) > 180,
                SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    // Aktif (izole) bolgenin adini merkeze parlak cizer. Izolasyon yokken
    // bolge adlari cizilmez (deniz/ulke etiketleriyle cakismasin).
    private void DrawRegionLabels(DrawingContext dc, Func<double, double, Point> project, double dpi)
    {
        if (_activeRegion == null) return;
        var grp = PipelineTopology.Nodes.Where(n => n.Region == _activeRegion).ToList();
        if (grp.Count == 0) return;
        var p = project(grp.Average(n => n.Lat), grp.Average(n => n.Lon));
        var ft = Text(RegionAd(_activeRegion).ToUpperInvariant(), 15,
            Color.FromArgb(210, 0x4E, 0xCD, 0xC4), dpi);
        dc.DrawText(ft, new Point(p.X - ft.Width / 2, p.Y - ft.Height / 2));
    }

    // Enlem/boylam koordinat ızgarasını çizer. Projeksiyon lat->Y, lon->X biçiminde
    // ayrık olduğundan meridyenler tam dikey, paraleller tam yatay çizgilerdir.
    // Aralık zoom'a göre sıklaşır; çizgiler soluk, kenarda derece etiketli.
    private void DrawGraticule(DrawingContext dc, Func<double, double, Point> project, double dpi)
    {
        double w = ActualWidth, h = ActualHeight;
        double step = _zoom < 1.5 ? 2 : _zoom < 3 ? 1 : _zoom < 6 ? 0.5 : 0.25;

        var linePen = new Pen(new SolidColorBrush(Color.FromArgb(40, MutedCol.R, MutedCol.G, MutedCol.B)), 0.6);
        var lblCol = Color.FromArgb(165, MutedCol.R, MutedCol.G, MutedCol.B);

        // Meridyenler (sabit boylam): X yalnız boylama bağlı -> dikey çizgi.
        for (double lon = Math.Ceiling(20 / step) * step; lon <= 50; lon += step)
        {
            double x = project(0, lon).X;
            if (x < 0 || x > w) continue;
            dc.DrawLine(linePen, new Point(x, 0), new Point(x, h));
            var ft = Text(FormatDeg(lon, isLat: false), 9.5, lblCol, dpi);
            dc.DrawText(ft, new Point(x + 3, 3));
        }
        // Paraleller (sabit enlem): Y yalnız enleme bağlı -> yatay çizgi.
        for (double lat = Math.Ceiling(28 / step) * step; lat <= 48; lat += step)
        {
            double y = project(lat, 0).Y;
            if (y < 0 || y > h) continue;
            dc.DrawLine(linePen, new Point(0, y), new Point(w, y));
            var ft = Text(FormatDeg(lat, isLat: true), 9.5, lblCol, dpi);
            dc.DrawText(ft, new Point(3, y + 2));
        }
    }

    // Koordinat etiketi: "37°D" (Doğu) / "40°K" (Kuzey). Ondalıklı adımda .5 gösterir.
    private static string FormatDeg(double v, bool isLat)
    {
        double a = Math.Abs(v);
        string num = a == Math.Floor(a) ? a.ToString("0", CultureInfo.InvariantCulture)
                                        : a.ToString("0.##", CultureInfo.InvariantCulture);
        string hemi = isLat ? (v >= 0 ? "K" : "G") : (v >= 0 ? "D" : "B");
        return $"{num}°{hemi}";
    }

    // Deniz / komsu ulke etiketlerini cizer (soluk, arka planda baglam).
    private void DrawGeoLabels(DrawingContext dc, Func<double, double, Point> project, double dpi)
    {
        foreach (var g in GeoLabels)
        {
            var p = project(g.Lat, g.Lon);
            if (p.X < -60 || p.Y < -40 || p.X > ActualWidth + 60 || p.Y > ActualHeight + 40) continue;

            (double size, Color col) = g.Kind switch
            {
                GeoKind.Sea => (12.5, Color.FromArgb(160, GeoSeaCol.R, GeoSeaCol.G, GeoSeaCol.B)),
                GeoKind.Country => (11.0, Color.FromArgb(140, GeoCountryCol.R, GeoCountryCol.G, GeoCountryCol.B)),
                _ => (10.5, Color.FromArgb(150, MutedCol.R, MutedCol.G, MutedCol.B)),
            };
            foreach (var (line, idx) in g.Name.Split('\n').Select((l, i) => (l, i)))
            {
                var ft = Text(line, size, col, dpi);
                dc.DrawText(ft, new Point(p.X - ft.Width / 2, p.Y - ft.Height / 2 + idx * (ft.Height + 1)));
            }
        }
    }

    // Verilen halkalari (her biri [lon,lat] noktali) tek dolgulu geometriye cevirir.
    private static Geometry BuildRingGeometry(IEnumerable<double[][]> rings, Func<double, double, Point> project)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            foreach (var ring in rings)
            {
                if (ring.Length < 3) continue;
                ctx.BeginFigure(project(ring[0][1], ring[0][0]), true, true);
                var pts = new List<Point>(ring.Length - 1);
                for (int i = 1; i < ring.Length; i++)
                    pts.Add(project(ring[i][1], ring[i][0]));
                ctx.PolyLineTo(pts, true, false);
            }
        }
        geo.Freeze();
        return geo;
    }

    // BuildRingGeometry gibi ama her koseyi 'radius' piksel yaricapinda kavise cevirir
    // (quadratik bezier). Blok/dikdortgen denizleri yumusatir; komsu-tarafi duz kenarlar
    // kalir ama sert 90° koseler kaybolur. Turkiye sinirlari icin kullanilmaz.
    private static Geometry BuildRoundedRingGeometry(IEnumerable<double[][]> rings, Func<double, double, Point> project, double radius)
    {
        // Nonzero: ust uste binen deniz halkalari delik acmadan birlesir.
        var geo = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var ctx = geo.Open())
        {
            foreach (var ring in rings)
            {
                if (ring.Length < 3) continue;
                int n = ring.Length;
                var pts = new Point[n];
                for (int i = 0; i < n; i++) pts[i] = project(ring[i][1], ring[i][0]);

                // Her kose icin giris (onceki kenardan) ve cikis (sonraki kenara) noktasi;
                // yaricap komsu kenarin yarisini asamaz (kisa kenarda tasmayi onler).
                var starts = new Point[n];
                var ends = new Point[n];
                for (int i = 0; i < n; i++)
                {
                    var cur = pts[i];
                    var toPrev = pts[(i - 1 + n) % n] - cur;
                    var toNext = pts[(i + 1) % n] - cur;
                    double lp = Math.Max(1e-6, toPrev.Length), ln = Math.Max(1e-6, toNext.Length);
                    starts[i] = cur + toPrev * (Math.Min(radius, lp / 2) / lp);
                    ends[i] = cur + toNext * (Math.Min(radius, ln / 2) / ln);
                }

                ctx.BeginFigure(ends[0], true, true);
                for (int i = 1; i <= n; i++)
                {
                    int idx = i % n;
                    ctx.LineTo(starts[idx], true, false);          // duz kenar
                    ctx.QuadraticBezierTo(pts[idx], ends[idx], true, false); // yumusak kose
                }
            }
        }
        geo.Freeze();
        return geo;
    }

    private static void DrawLabel(DrawingContext dc, FormattedText ft, Point at)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(150, Bg.R, Bg.G, Bg.B)), null,
            new Rect(at.X - 3, at.Y, ft.Width + 6, ft.Height + 1));
        dc.DrawText(ft, at);
    }

    private static FormattedText Text(string s, double size, Color c, double dpi) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, new SolidColorBrush(c), dpi);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragging = true; _moved = false;
        _dragStart = e.GetPosition(this);
        _panStartX = _panX; _panStartY = _panY;
        CaptureMouse();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _dragging = false; ReleaseMouseCapture();
        if (_moved) return; // sürüklemeyse tıklama sayma

        // Secim balonu aciksa: bir satira tiklandiysa onu sec, degilse balonu kapat.
        if (_cluster != null)
        {
            PNode? chosen = _clusterHover >= 0 && _clusterHover < _cluster.Count ? _cluster[_clusterHover] : null;
            _cluster = null; _clusterHover = -1;
            if (chosen != null) NodeClicked?.Invoke(chosen);
            InvalidateVisual();
            return;
        }

        var click = e.GetPosition(this);
        foreach (var n in PipelineTopology.Nodes)
            if (!IsHidden(n) && IsInteractive(n) && _screen.TryGetValue(n.Id, out var p) && (click - p).Length <= 26)
            { NodeClicked?.Invoke(n); break; }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        _mouse = e.GetPosition(this);
        if (_dragging)
        {
            var d = _mouse - _dragStart;
            if (d.Length > 3) _moved = true;
            _panX = _panStartX + d.X; _panY = _panStartY + d.Y;
            _hoverId = null; _cluster = null;
            InvalidateVisual();
            return;
        }

        // Acik secim balonu: fare balonun/gecisin uzerindeyken acik tut, satiri isaretle.
        if (_cluster != null)
        {
            bool inBox = _clusterBox.Contains(_mouse);
            bool keep = inBox || Rect.Inflate(_clusterBox, 24, 24).Contains(_mouse)
                        || (_mouse - _clusterAnchor).Length <= 22;
            if (keep)
            {
                int idx = inBox && _clusterRowH > 0
                    ? (int)((_mouse.Y - (_clusterBox.Y + _clusterHeaderH)) / _clusterRowH) : -1;
                _clusterHover = (idx >= 0 && idx < _cluster.Count) ? idx : -1;
                Cursor = _clusterHover >= 0 ? Cursors.Hand : Cursors.Arrow;
                InvalidateVisual();
                return;
            }
            _cluster = null; // balondan uzaklasti -> kapat
        }

        // Fare cevresindeki gorunur dugumler (ust uste binenler dahil).
        var near = NodesNear(_mouse, 18);
        if (near.Count > 1) // birden fazla -> secim balonu ac
        {
            _cluster = near; _clusterAnchor = _mouse; _clusterHover = -1; _hoverId = null;
            Cursor = Cursors.Hand; InvalidateVisual(); return;
        }

        string? prevHover = _hoverId;
        _hoverId = null;
        if (near.Count == 1)
        {
            _hoverId = near[0].Id; _hoverSeg = false;
            if (_hoverId != prevHover) _hoverStartTick = Environment.TickCount;
            Cursor = Cursors.Hand; // tiklanabilir gostergesi
            InvalidateVisual();
            return;
        }
        foreach (var s in PipelineTopology.Segments)
            if (_segScreen.TryGetValue(s.Id, out var pts) && DistToPolyline(_mouse, pts) <= 7)
            { _hoverId = s.Id; _hoverSeg = true; Cursor = Cursors.Hand; InvalidateVisual(); return; }

        Cursor = Cursors.Arrow; // bos alan
    }

    // Fare cevresindeki (rad px) gorunur dugumler, mesafeye gore sirali (en fazla 8).
    private List<PNode> NodesNear(Point m, double rad)
    {
        var list = new List<PNode>();
        foreach (var n in PipelineTopology.Nodes)
            if (!IsHidden(n) && IsInteractive(n) && _screen.TryGetValue(n.Id, out var p) && (m - p).Length <= rad)
                list.Add(n);
        list.Sort((a, b) => (m - _screen[a.Id]).Length.CompareTo((m - _screen[b.Id]).Length));
        if (list.Count > 8) list.RemoveRange(8, list.Count - 8);
        return list;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hoverId = null; _cluster = null; Cursor = Cursors.Arrow; InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        double f = e.Delta > 0 ? 1.15 : 1 / 1.15;
        // Alt sinir 0.45: komsu ulkeleri/denizleri gormek icin daha genis uzaklas.
        double nz = Math.Clamp(_zoom * f, 0.45, 8.0);
        f = nz / _zoom;
        var m = e.GetPosition(this);
        _panX = m.X - (m.X - _panX) * f;   // imlec altindaki nokta sabit kalir
        _panY = m.Y - (m.Y - _panY) * f;
        _zoom = nz;
        InvalidateVisual();
    }

    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        FitTurkey(); // eskisi gibi: Turkiye'yi cerceveler (genisletilmis menzili degil)
    }

    /// <summary>
    /// Turkiye il kutusunu tam-gorunum boyutunda cerceveler ve ortalar. Projeksiyon
    /// menzili canli feed'le kuzeye (or. ~48°K sinir dugumu) genisletilmis olsa bile
    /// harita hep Turkiye'ye odaklanir; menzil disi kuzey dugumleri gorunum disinda
    /// kalir (teker ile uzaklasilir). _fitZoom OnRender'da hesaplanir.
    /// </summary>
    public void FitTurkey()
    {
        EnsureBbox();
        double w = ActualWidth, h = ActualHeight;
        if (!_geoReady || _maxLon <= _minLon || w < 40 || h < 40)
        { _zoom = 1; _panX = 0; _panY = 0; InvalidateVisual(); return; }

        var c = GeoAt((_minLat + _maxLat) / 2, (_minLon + _maxLon) / 2); // taban ekran merkezi
        _zoom = _fitZoom;
        _panX = w / 2 - c.X * _zoom;
        _panY = h / 2 - c.Y * _zoom;
        InvalidateVisual();
    }

    /// <summary>Verilen cografi kutuya yumusakca odaklan (bolge izolasyonu).</summary>
    public void FocusBounds(double minLat, double maxLat, double minLon, double maxLon, double marginFrac = 0.25)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 40 || h < 40 || !_geoReady) return;

        // Kutunun tam-gorunum (zoom=1) ekran koordinatlari.
        var a = GeoAt(maxLat, minLon); var b = GeoAt(minLat, maxLon);
        double bw = Math.Abs(b.X - a.X), bh = Math.Abs(b.Y - a.Y);
        if (bw < 1 || bh < 1) return;

        double target = Math.Clamp(Math.Min(w / (bw * (1 + marginFrac)), h / (bh * (1 + marginFrac))), 0.45, 8.0);
        double cx = (a.X + b.X) / 2, cy = (a.Y + b.Y) / 2;
        _zoom = target;
        _panX = w / 2 - cx * target;
        _panY = h / 2 - cy * target;
        InvalidateVisual();
    }

    /// <summary>Tam Turkiye gorunumune don.</summary>
    public void ResetView() { _activeRegion = null; FitTurkey(); }

    // Aktif (izole) bolge: digerleri soluklasir, o bolgeye odaklanilir.
    private string? _activeRegion;

    // Bolge anahtari -> Turkce ad (UI ve etiketler icin).
    public static readonly (string Key, string Ad)[] Regions =
    {
        ("marmara", "Marmara"), ("ege", "Ege"), ("akdeniz", "Akdeniz"),
        ("ic_anadolu", "İç Anadolu"), ("orta_anadolu", "Orta Anadolu"),
        ("karadeniz", "Karadeniz"), ("dogu_anadolu", "Doğu Anadolu"),
        ("guneydogu_anadolu", "Güneydoğu Anadolu"),
    };

    private static string RegionAd(string key)
    {
        foreach (var (k, ad) in Regions) if (k == key) return ad;
        return key;
    }

    /// <summary>Bir bolgeyi izole et: o bolgenin dugumlerine odaklan, digerlerini soluklastir.</summary>
    public void FocusRegion(string regionKey)
    {
        var pts = PipelineTopology.Nodes.Where(n => n.Region == regionKey).ToList();
        if (pts.Count == 0) return;
        _activeRegion = regionKey;
        FocusBounds(pts.Min(n => n.Lat), pts.Max(n => n.Lat),
                    pts.Min(n => n.Lon), pts.Max(n => n.Lon), 0.35);
    }

    /// <summary>Polyline uzerinde bastan itibaren verilen mesafedeki nokta.</summary>
    private static Point PointAlong(Point[] pts, double dist) => PointAlong(pts, dist, out _);

    private static Point PointAlong(Point[] pts, double dist, out Vector dir)
    {
        dir = new Vector(1, 0);
        for (int i = 0; i + 1 < pts.Length; i++)
        {
            var d = pts[i + 1] - pts[i];
            double len = d.Length;
            if (len < 1e-9) continue;
            if (dist <= len || i + 2 == pts.Length)
            {
                dir = d / len;
                double t = Math.Clamp(dist / len, 0, 1);
                return pts[i] + d * t;
            }
            dist -= len;
        }
        return pts[^1];
    }

    /// <summary>Noktanin polyline'a en kisa mesafesi (hover icin).</summary>
    private static double DistToPolyline(Point p, Point[] pts)
    {
        double best = double.MaxValue;
        for (int i = 0; i + 1 < pts.Length; i++)
            best = Math.Min(best, DistToSeg(p, pts[i], pts[i + 1]));
        return best;
    }

    private static double DistToSeg(Point p, Point a, Point b)
    {
        var ab = b - a; double len2 = ab.LengthSquared;
        if (len2 < 1e-6) return (p - a).Length;
        double t = Math.Clamp(((p - a) * ab) / len2, 0, 1);
        return (p - (a + ab * t)).Length;
    }

    private static readonly Color AccentCol = Color.FromRgb(0x4E, 0xCD, 0xC4);

    // Yeniden tasarlanan ipucu kutusu: baslik (renkli nokta + ad), altta ayrinti
    // satirlari; yuvarlak koseli, golgeli, imlecin ustune tasmayan konumda.
    private void DrawTooltip(DrawingContext dc, double dpi)
    {
        if (_hoverId == null || _source == null) return;

        Color dot; string title, sub; string[] rows;
        if (_hoverSeg)
        {
            var s = PipelineTopology.Segments.First(x => x.Id == _hoverId);
            var snap = _source.Segment(s.Id);
            dot = Lerp(AccentCol, Color.FromRgb(0xFF, 0x6B, 0x6B), snap.LoadRatio);
            title = s.Id; sub = $"{s.From} → {s.To}";
            rows = snap.Leak
                ? new[] { $"Akış: {snap.FlowMcmDay:0} mcm/gün", "⚠ SIZINTI" }
                : new[] { $"Akış: {snap.FlowMcmDay:0} mcm/gün" };
        }
        else
        {
            var n = PipelineTopology.NodeById(_hoverId);
            if (n is null) return; // hover id düğüm listesinde yoksa balon çizme
            title = n.Name; sub = n.Id;
            if (n.IsStation)
            {
                var snap = _source.Node(n.Id);
                var units = _source.UnitHealths(n.Id);
                // Basliktaki saglik = gorunum moduyla tutarli ozet (pasta → kenar kurali).
                double shown = _healthMode == HealthDisplayMode.Average
                    ? HealthAgg.Combine(units, HealthAggregate.Average, snap.Health)
                    : _healthMode == HealthDisplayMode.Pie
                        ? HealthAgg.Combine(units, _outlineAgg, snap.Health)
                        : snap.Health;
                dot = HealthColor(shown);
                string durum = shown < _criticalThreshold ? "Kritik"
                             : shown >= 70 ? "Sağlıklı"
                             : shown >= 40 ? "Uyarı" : "Riskli";
                var rowList = new List<string> { $"Durum: {durum}   %{shown:0}", $"RUL: {snap.Rul:0} döngü" };
                // Pasta modunda ünite başına kırılım.
                if (_healthMode == HealthDisplayMode.Pie && units.Count > 1)
                    foreach (var u in units)
                        rowList.Add(u.HasTelemetry ? $"  {u.UnitId}: %{u.Health:0}" : $"  {u.UnitId}: veri yok");
                rows = rowList.ToArray();
            }
            else if (n.Type == "STORAGE")
            { dot = NodeTypeColor(n.Type); rows = new[] { $"Depo doluluk: %{_source.Level(n.Id):0}" }; }
            else
            { dot = NodeTypeColor(n.Type); rows = new[] { TypeLabel(n.Type) }; }
        }

        var titleFt = Text(title, 12.5, TextCol, dpi); titleFt.SetFontWeight(FontWeights.SemiBold);
        var subFt = Text(sub, 10, MutedCol, dpi);
        var rowFts = rows.Select(r => Text(r, 11, r.StartsWith("⚠") ? Color.FromRgb(0xE7, 0x4C, 0x3C) : MutedCol, dpi)).ToList();

        double pad = 10, dotW = 14, gap = 4;
        double contentW = Math.Max(dotW + titleFt.Width, subFt.Width);
        foreach (var rf in rowFts) contentW = Math.Max(contentW, rf.Width);
        double w = contentW + 2 * pad;
        double h = pad + titleFt.Height + gap + subFt.Height + 6 + rowFts.Sum(r => r.Height + 2) + pad;

        double x = _mouse.X + 16, y = _mouse.Y + 16;
        if (x + w > ActualWidth - 4) x = _mouse.X - w - 16;
        if (y + h > ActualHeight - 4) y = ActualHeight - h - 4;
        var rect = new Rect(x, y, w, h);

        // Golge + govde (yuvarlak kose).
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), null,
            new Rect(x + 2, y + 3, w, h), 8, 8);
        dc.DrawRoundedRectangle(new SolidColorBrush(TipBg),
            new Pen(new SolidColorBrush(TipBorder), 1), rect, 8, 8);

        double cy = y + pad;
        dc.DrawEllipse(new SolidColorBrush(dot), null, new Point(x + pad + 5, cy + titleFt.Height / 2), 5, 5);
        dc.DrawText(titleFt, new Point(x + pad + dotW, cy));
        cy += titleFt.Height + gap;
        dc.DrawText(subFt, new Point(x + pad, cy));
        cy += subFt.Height + 6;
        foreach (var rf in rowFts) { dc.DrawText(rf, new Point(x + pad, cy)); cy += rf.Height + 2; }
    }

    // Ust uste binen dugumler icin secim balonu: her nesneyi renkli nokta + ad + tur
    // ile listeler; fare altindaki satir vurgulanir, tiklayinca o dugum secilir.
    private void DrawClusterChooser(DrawingContext dc, double dpi)
    {
        if (_cluster == null || _cluster.Count < 2 || _source == null) return;

        var header = Text($"{_cluster.Count} nesne — birini seçin", 10.5, MutedCol, dpi);
        var names = _cluster.Select(n => Text(n.Name, 11.5, TextCol, dpi)).ToList();
        var kinds = _cluster.Select(n => Text(NodeKindLabel(n), 9.5, MutedCol, dpi)).ToList();

        const double pad = 8, dotW = 16, rowPad = 6;
        _clusterRowH = names[0].Height + kinds[0].Height + rowPad * 2;
        _clusterHeaderH = header.Height + 8;

        double contentW = header.Width;
        for (int i = 0; i < _cluster.Count; i++)
            contentW = Math.Max(contentW, dotW + Math.Max(names[i].Width, kinds[i].Width));
        double w = contentW + 2 * pad;
        double h = _clusterHeaderH + _clusterRowH * _cluster.Count + pad;

        double x = _clusterAnchor.X + 16, y = _clusterAnchor.Y + 12;
        if (x + w > ActualWidth - 4) x = _clusterAnchor.X - w - 16;
        if (x < 4) x = 4;
        if (y + h > ActualHeight - 4) y = ActualHeight - h - 4;
        if (y < 4) y = 4;
        _clusterBox = new Rect(x, y, w, h);

        // Baglanti cizgisi: capa noktasindan balona (hangi kume oldugu belli olsun).
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(120, AccentCol.R, AccentCol.G, AccentCol.B)), 1),
            _clusterAnchor, new Point(x + 10, y + 10));
        dc.DrawEllipse(new SolidColorBrush(AccentCol), null, _clusterAnchor, 3, 3);

        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), null, new Rect(x + 2, y + 3, w, h), 8, 8);
        dc.DrawRoundedRectangle(new SolidColorBrush(TipBg), new Pen(new SolidColorBrush(TipBorder), 1), _clusterBox, 8, 8);
        dc.DrawText(header, new Point(x + pad, y + 4));

        double ry = y + _clusterHeaderH;
        for (int i = 0; i < _cluster.Count; i++)
        {
            var n = _cluster[i];
            if (i == _clusterHover)
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(45, AccentCol.R, AccentCol.G, AccentCol.B)),
                    null, new Rect(x + 3, ry, w - 6, _clusterRowH), 5, 5);
            double hh = n.IsStation ? _source.Node(n.Id).Health : 100;
            var col = n.IsStation ? ((SolidColorBrush)HealthBrush(hh)).Color : NodeTypeColor(n.Type);
            dc.DrawEllipse(new SolidColorBrush(col), null, new Point(x + pad + 4, ry + rowPad + names[i].Height / 2), 5, 5);
            dc.DrawText(names[i], new Point(x + pad + dotW, ry + rowPad));
            dc.DrawText(kinds[i], new Point(x + pad + dotW, ry + rowPad + names[i].Height));
            ry += _clusterRowH;
        }
    }

    private static string NodeKindLabel(PNode n) =>
        n.IsStation || n.Type == "CS" ? "Kompresör İstasyonu" : TypeLabel(n.Type);

    private static string TypeLabel(string type) => type switch
    {
        "BORDER" => "Sınır İstasyonu",
        "OFFTAKE" => "Şehir Gaz Çıkışı",
        "JUNCTION" => "Dağıtım Kavşağı",
        "STORAGE" => "Yeraltı Depolama",
        "LNG" => "LNG Terminali",
        "FSRU" => "FSRU",
        "TERMINAL" => "Geçiş İstasyonu",
        "PS" or "PT" => "Petrol Pompa İstasyonu",
        "OILDEPO" => "Petrol Depolama/Yükleme Tesisi",
        _ => type,
    };
}
