namespace VolumeRendering.Runtime.Segmentation
{
    // Parametri magic brusha. Zadane vrijednosti su iz tablice "Zadani parametri" (2D stupac) u
    // docs/magic-brush-research.md, poglavlje 4. Polazište za štimanje u S8.
    public sealed class MagicBrushSettings
    {
        // Osjetljivost k: tolerancija t = k·σ (uobičajeno 0,5–5). Korisnik vidi samo ovaj klizač.
        public double Sensitivity { get; set; } = 2.5;

        // Granice tolerancije u HU. Za MR HU nema smisla, ali je pretvorba ista
        // (1 HU ≈ 16,4 jedinice ushort gustoće), pa vrijednosti i dalje znače "širinu intervala".
        public double MinToleranceHu { get; set; } = 15;
        public double MaxToleranceHu { get; set; } = 300;

        // Prozor za statistiku oko sjemena (neparna veličina, 5 = 5×5).
        public int SeedWindow { get; set; } = 5;

        // true = medijan i MAD·1,4826 umjesto μ i σ (opcija "robusno").
        public bool RobustStatistics { get; set; } = false;

        // Sjeme klizi najviše ovoliko piksela od klika, na piksel s najmanjim gradijentom u ravnini.
        public int SeedSlidePixels { get; set; } = 2;

        // Najveći radijus regije (ROI krug) u mm.
        public double MaxRadiusMm { get; set; } = 60;

        // Anti-leak radijus erozije u mm (0 = isključeno). Mostovi uži od 2r+1 piksela se prekinu.
        public double AntiLeakRadiusMm { get; set; } = 2;

        // Rubni stop: piksel ulazi samo ako mu je gradijent u ravnini ≤ ova vrijednost.
        // Ista skala kao G kanal (0–1, |∇gustoće|·10). 0 = isključeno (zadano u 2D).
        // Rubni stop zaustavlja rast; sloj uz sam rub (visok gradijent, ali gustoća u intervalu) se na
        // kraju vrati jednim korakom dilatacije, da regija ne izgubi rub.
        public double EdgeStop { get; set; } = 0;

        // Zatvaranje rupa u rezultatu.
        public bool FillHoles { get; set; } = true;

        // Zatvaraju se samo rupe do ove površine u mm² (u 3D: po sliceu). 0 = sve rupe.
        // Bez granice klik u masnoću oko organa "zatvori" i organ kao rupu. 200 mm² ≈ krug promjera 16 mm.
        public double MaxHoleAreaMm2 { get; set; } = 200;

        // Samo 3D (RegionGrow3D): najviše voksela u regiji. Iznad toga rezultat je prazan, a
        // Diagnostics javlja "smanji osjetljivost". 2D ga ne čita (ROI krug ionako ograničava).
        public long MaxVoxels { get; set; } = 3000000;

        public MagicBrushSettings Clone() => (MagicBrushSettings)MemberwiseClone();

        // Zadane vrijednosti za 3D (stupac 3D iz tablice "Zadani parametri", poglavlje 4).
        // Konstruktor (new MagicBrushSettings()) i dalje daje 2D zadane vrijednosti.
        public static MagicBrushSettings Default3D() => new MagicBrushSettings
        {
            SeedWindow = 3,          // 3×3×3
            SeedSlidePixels = 1,     // do 1 voksel
            MaxRadiusMm = 40,
            EdgeStop = 0.6,          // G kanal
            MaxVoxels = 3000000,
        };
    }
}
