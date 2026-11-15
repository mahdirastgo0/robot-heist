using System.Collections.Generic;
using UnityEngine;

// ساخت دنیا با کد: شهر با ساختمان‌های جزئیات‌دار، بیس، بازیکن و پلیس‌ها
public partial class GameManager
{
    static readonly string[] ShopNames = { "Grocery", "Electronics", "Jewelry Store", "Bank" };
    static readonly Color[] ShopColors =
    {
        new Color(0.3f, 0.85f, 0.35f), new Color(0.3f, 0.55f, 1f), new Color(1f, 0.85f, 0.2f), new Color(0.85f, 0.85f, 0.9f)
    };
    static readonly float[] ShopTime = { 2f, 3f, 3.5f, 5.5f };
    static readonly Color MarketColor = new Color(0.75f, 0.3f, 1f);
    static readonly Color LitWindow = new Color(1f, 0.86f, 0.5f);

    Transform scn;   // دکور ثابت؛ آخرش با StaticBatching ترکیب میشه
    readonly Dictionary<int, Material> matCache = new Dictionary<int, Material>();

    // ---------------------------------------------------------------- Scene basics
    // متریال‌ها بر اساس رنگ cache میشن تا batching کار کنه
    Material Mat(Color c)
    {
        if (shader == null)
        {
            var t = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shader = t.GetComponent<Renderer>().sharedMaterial.shader;   // سازگار با Built-in و URP
            Destroy(t);
        }
        Color32 k = c;
        int key = (k.r << 24) | (k.g << 16) | (k.b << 8) | k.a;
        Material m;
        if (!matCache.TryGetValue(key, out m) || m == null)
        {
            m = new Material(shader);
            m.color = c;
            matCache[key] = m;
        }
        return m;
    }

    GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Color c, bool withCollider = false)
    {
        var g = GameObject.CreatePrimitive(type);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = Mat(c);
        if (!withCollider) { var col = g.GetComponent<Collider>(); if (col != null) Destroy(col); }
        return g;
    }

    GameObject Box(Vector3 pos, Vector3 size, Color c, bool col = false) { return Prim(PrimitiveType.Cube, "Box", scn, pos, size, c, col); }
    GameObject Cyl(Vector3 pos, Vector3 scale, Color c) { return Prim(PrimitiveType.Cylinder, "Cyl", scn, pos, scale, c); }
    GameObject Ball(Vector3 pos, Vector3 scale, Color c) { return Prim(PrimitiveType.Sphere, "Ball", scn, pos, scale, c); }

    void ResetRoot()
    {
        if (root != null) Destroy(root.gameObject);
        root = new GameObject("Level").transform;
        scn = new GameObject("Scenery").transform;
        scn.SetParent(root, false);
        spots.Clear(); cops.Clear();
        robbingSpot = null; robProgress = 0f; hint = "";
    }

    void CombineScenery()
    {
        try { StaticBatchingUtility.Combine(scn.gameObject); }
        catch (System.Exception e) { Debug.LogWarning("Static batching skipped: " + e.Message); }
    }

    Spot MakeSpot(SpotType type, Vector3 pos, Color c, string label, int shopType = 0, float duration = 0f)
    {
        var s = new Spot { type = type, pos = pos, label = label, shopType = shopType, duration = duration, color = c };
        s.pad = Prim(PrimitiveType.Cylinder, "Pad", root, pos + Vector3.up * 0.05f, new Vector3(2.4f, 0.03f, 2.4f), c);
        s.beam = Prim(PrimitiveType.Cylinder, "Beam", root, pos + Vector3.up * 3f, new Vector3(0.2f, 3f, 0.2f), c);
        spots.Add(s);
        return s;
    }

    // ---------------------------------------------------------------- City grid
    // جاده‌ها: x%6==0 یا y%6==0 | پیاده‌رو: حلقه‌ی دور هر بلوک | ساختمان ۳×۳ وسط هر بلوک
    void BuildCityGrid()
    {
        blocked = new bool[CW, CH];
        walkCells.Clear(); buildingMins.Clear();
        for (int y = 0; y < CH; y++)
            for (int x = 0; x < CW; x++)
            {
                bool b = (x % 6 >= 2 && x % 6 <= 4) && (y % 6 >= 2 && y % 6 <= 4);
                blocked[x, y] = b;
                if (!b) walkCells.Add(new Vector2Int(x, y));
            }
        for (int by = 2; by < CH; by += 6)
            for (int bx = 2; bx < CW; bx += 6)
                buildingMins.Add(new Vector2Int(bx, by));
    }

    string CityMapString()
    {
        var sb = new System.Text.StringBuilder();
        for (int y = 0; y < CH; y++)
            for (int x = 0; x < CW; x++)
                sb.Append(blocked[x, y] ? '#' : '.');
        return sb.ToString();
    }

    // ---------------------------------------------------------------- City
    void BuildCity(int seed)
    {
        ResetRoot();
        var rng = new System.Random(seed);
        float mid = (CW - 1) * Cell / 2f;

        TexBox(new Vector3(mid, -0.1f, -mid), new Vector3(110f, 0.2f, 110f), TexMat("asphalt", Color.white, 0.08f), 8f, true);

        // پیاده‌رو
        for (int j = 0; j < 4; j++)
            for (int i = 0; i < 4; i++)
            {
                var c = CellToWorld(new Vector2Int(6 * i + 3, 6 * j + 3));
                TexBox(new Vector3(c.x, 0f, c.z), new Vector3(5f * Cell, 0.12f, 5f * Cell), TexMat("pavement", new Color(1f, 1f, 1f), 0.1f), 4f, true);
            }

        // حصار دور شهر
        Color fence = new Color(0.1f, 0.1f, 0.12f);
        var fenceMat = TexMat("concrete", new Color(0.75f, 0.75f, 0.78f), 0.1f);
        TexBox(new Vector3(-3f, 1.5f, -mid), new Vector3(1f, 3f, 110f), fenceMat, 3f, true);
        TexBox(new Vector3(99f, 1.5f, -mid), new Vector3(1f, 3f, 110f), fenceMat, 3f, true);
        TexBox(new Vector3(mid, 1.5f, 3f), new Vector3(110f, 3f, 1f), fenceMat, 3f, true);
        TexBox(new Vector3(mid, 1.5f, -99f), new Vector3(110f, 3f, 1f), fenceMat, 3f, true);

        BuildRoadMarkings();
        BuildStreetProps(rng);

        // نقش ساختمان‌ها: 0 خواروبار×4، 1 الکترونیک×4، 2 جواهری×3، 3 بانک×1، 4 بلک‌مارکت×1، 5 معمولی×3
        int[] roles = { 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 3, 4, 5, 5, 5 };
        for (int i = roles.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            int t = roles[i]; roles[i] = roles[j]; roles[j] = t;
        }
        marketMult = Mathf.Round((1.0f + (float)rng.NextDouble() * 0.5f) * 100f) / 100f;

        for (int i = 0; i < buildingMins.Count; i++)
        {
            var bmin = buildingMins[i];
            int role = roles[i];
            BuildBuilding(bmin, role, rng);
            if (role == 5) continue;

            Vector3 door = CellToWorld(new Vector2Int(bmin.x + 1, bmin.y + 3));
            var news = PropModels.Load("Models/Props/newspaper");
            if (news != null && role != 4 && rng.Next(0, 2) == 0)
                PropModels.Spawn(news, scn, door + new Vector3(1.7f, 0f, 0.4f), rng.Next(0, 4) * 90f, 1f, false, Color.white, true);
            if (role == 4) MakeSpot(SpotType.Market, door, MarketColor, "BLACK MARKET");
            else MakeSpot(SpotType.Shop, door, ShopColors[role], ShopNames[role], role, ShopTime[role]);
        }

        // ایستگاه اتوبوس کنار دو جاده
        var bus = PropModels.Load("Models/Props/busstop");
        if (bus != null)
        {
            Vector3 b1 = CellToWorld(new Vector2Int(7, 15)) + new Vector3(-1.1f, 0f, 0f);
            Vector3 b2 = CellToWorld(new Vector2Int(17, 9)) + new Vector3(1.1f, 0f, 0f);
            PropModels.Spawn(bus, scn, b1, 0f, 1f, false, Color.white, true);
            PropModels.Spawn(bus, scn, b2, 180f, 1f, false, Color.white, true);
        }

        // دروازه‌ی خروجی (لبه‌ی شرقی)
        Vector3 ex = CellToWorld(new Vector2Int(CW - 1, 12));
        Color gate = new Color(0.2f, 1f, 0.4f);
        Box(new Vector3(ex.x, 3f, ex.z - 3.2f), new Vector3(0.6f, 6f, 0.6f), new Color(0.25f, 0.25f, 0.28f));
        Box(new Vector3(ex.x, 3f, ex.z + 3.2f), new Vector3(0.6f, 6f, 0.6f), new Color(0.25f, 0.25f, 0.28f));
        Box(new Vector3(ex.x, 6.1f, ex.z), new Vector3(0.6f, 0.6f, 7.4f), gate);
        MakeSpot(SpotType.Exit, ex, gate, "EXIT -> BASE");

        CombineScenery();

        // بازیکن و پلیس‌های اولیه (لبه‌ی غربی، رو به شرق)
        CreatePlayer(CellToWorld(new Vector2Int(0, 12)) + Vector3.up * 0.1f, 90f);
        SpawnCops(3);
    }

    void BuildRoadMarkings()
    {
        Color line = new Color(0.85f, 0.8f, 0.5f);
        for (int y = 0; y < CH; y++)
            for (int x = 0; x < CW; x++)
            {
                bool h = y % 6 == 0, v = x % 6 == 0;
                if (h == v) continue;            // چهارراه یا بلوک
                Vector3 p = CellToWorld(new Vector2Int(x, y));
                if (h) Box(new Vector3(p.x, 0.015f, p.z), new Vector3(2f, 0.03f, 0.15f), line);
                else Box(new Vector3(p.x, 0.015f, p.z), new Vector3(0.15f, 0.03f, 2f), line);
            }
    }

    // چراغ خیابان، درخت و ماشین‌های پارک‌شده
    void BuildStreetProps(System.Random rng)
    {
        Color pole = new Color(0.2f, 0.2f, 0.23f);
        Color lamp = new Color(1f, 0.92f, 0.65f);
        var trees = new List<PropData>();
        for (int ti = 1; ti <= 6; ti++) { var td = PropModels.Load("Models/Props/tree_" + ti); if (td != null) trees.Add(td); }
        var hydrant = PropModels.Load("Models/Props/hydrant");
        for (int j = 0; j < 4; j++)
            for (int i = 0; i < 4; i++)
            {
                // چراغ: دو گوشه‌ی مقابل هر بلوک
                Vector3 a = CellToWorld(new Vector2Int(6 * i + 1, 6 * j + 1)) + new Vector3(-1.4f, 0f, 1.4f);
                Vector3 b = CellToWorld(new Vector2Int(6 * i + 5, 6 * j + 5)) + new Vector3(1.4f, 0f, -1.4f);
                foreach (var p in new[] { a, b })
                {
                    Cyl(new Vector3(p.x, 2.4f, p.z), new Vector3(0.12f, 2.4f, 0.12f), pole);
                    Ball(new Vector3(p.x, 5f, p.z), Vector3.one * 0.55f, lamp);
                }
                if (hydrant != null)
                {
                    Vector3 hp = CellToWorld(new Vector2Int(6 * i + 5, 6 * j + 1)) + new Vector3(1.2f, 0f, 1.2f);
                    PropModels.Spawn(hydrant, scn, hp, rng.Next(0, 4) * 90f, 1f, false, Color.white, false);
                }

                // درخت: ضلع غربی، شرقی و شمالی بلوک (ضلع جنوبی درِ مغازه‌هاست)
                var tp = new[]
                {
                    CellToWorld(new Vector2Int(6 * i + 1, 6 * j + 3)) + new Vector3(-1.3f, 0f, 0f),
                    CellToWorld(new Vector2Int(6 * i + 5, 6 * j + 3)) + new Vector3(1.3f, 0f, 0f),
                    CellToWorld(new Vector2Int(6 * i + 3, 6 * j + 1)) + new Vector3(0f, 0f, 1.3f)
                };
                foreach (var p in tp)
                {
                    var tree = trees.Count > 0 ? trees[rng.Next(0, trees.Count)] : null;
                    if (tree != null)
                    {
                        PropModels.Spawn(tree, scn, new Vector3(p.x, 0f, p.z), rng.Next(0, 360), 0.8f + (float)rng.NextDouble() * 0.4f, false, Color.white, false);
                        continue;
                    }
                    Cyl(new Vector3(p.x, 1f, p.z), new Vector3(0.28f, 1f, 0.28f), new Color(0.35f, 0.24f, 0.15f));
                    float g = 0.4f + (float)rng.NextDouble() * 0.2f;
                    Ball(new Vector3(p.x, 3.1f, p.z), new Vector3(2.4f, 2.2f, 2.4f), new Color(0.2f, g, 0.22f));
                }
            }

        // ماشین‌های پارک‌شده کنار جاده (به‌جز چهارراه‌ها)
        Color[] carCols =
        {
            new Color(0.75f, 0.1f, 0.1f), new Color(0.1f, 0.3f, 0.7f), new Color(0.85f, 0.85f, 0.85f),
            new Color(0.1f, 0.1f, 0.12f), new Color(0.9f, 0.7f, 0.1f), new Color(0.2f, 0.5f, 0.3f)
        };
        var placed = new List<Vector3>();
        for (int tries = 0, n = 0; tries < 200 && n < 12; tries++)
        {
            int x = rng.Next(0, CW), y = rng.Next(0, CH);
            bool h = y % 6 == 0, v = x % 6 == 0;
            if (h == v) continue;
            Vector3 p = CellToWorld(new Vector2Int(x, y));
            float side = rng.Next(0, 2) == 0 ? 1.1f : -1.1f;
            p += h ? new Vector3(0f, 0f, side) : new Vector3(side, 0f, 0f);
            bool near = false;
            foreach (var q in placed) if ((q - p).magnitude < 5f) near = true;
            if (near) continue;
            placed.Add(p);
            n++;
            MakeCar(p, h ? 0f : 90f, carCols[rng.Next(0, carCols.Length)]);
        }
    }

    void MakeCar(Vector3 pos, float yaw, Color col)
    {
        var def = PropModels.Load("Models/Props/defender");
        if (def != null)
        {
            // yaw قدیمی: 0 = راستای X، 90 = راستای Z. مدل Defender جلوش +Z هست.
            float y = (90f - yaw) + (Random.value < 0.5f ? 0f : 180f);
            PropModels.Spawn(def, scn, pos, y, 1f, true, col, true);
            return;
        }
        var car = new GameObject("Car").transform;
        car.SetParent(scn, false);
        car.localPosition = pos;
        car.localRotation = Quaternion.Euler(0f, yaw, 0f);
        Prim(PrimitiveType.Cube, "CarBody", car, new Vector3(0f, 0.65f, 0f), new Vector3(3.9f, 0.7f, 1.8f), col, true);
        Prim(PrimitiveType.Cube, "CarCabin", car, new Vector3(-0.2f, 1.25f, 0f), new Vector3(2.1f, 0.6f, 1.6f), new Color(0.15f, 0.2f, 0.28f), true);
        Color tire = new Color(0.06f, 0.06f, 0.07f);
        foreach (float wx in new[] { -1.25f, 1.25f })
            foreach (float wz in new[] { -0.9f, 0.9f })
            {
                var w = Prim(PrimitiveType.Cylinder, "Wheel", car, new Vector3(wx, 0.32f, wz), new Vector3(0.64f, 0.1f, 0.64f), tire);
                w.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
        Prim(PrimitiveType.Cube, "HeadLights", car, new Vector3(1.96f, 0.7f, 0f), new Vector3(0.05f, 0.18f, 1.4f), new Color(1f, 0.95f, 0.7f));
        Prim(PrimitiveType.Cube, "TailLights", car, new Vector3(-1.96f, 0.7f, 0f), new Vector3(0.05f, 0.18f, 1.4f), new Color(0.8f, 0.05f, 0.05f));
    }

    // ---------------------------------------------------------------- Buildings
    void BuildBuilding(Vector2Int bmin, int role, System.Random rng)
    {
        Vector3 ctr = CellToWorld(new Vector2Int(bmin.x + 1, bmin.y + 1));
        const float half = 5.8f;
        int floors = role == 3 ? 4 : 2 + rng.Next(0, 3);
        float h = 3.6f * floors;

        Color wall;
        switch (role)
        {
            case 0: wall = new Color(0.82f, 0.74f, 0.58f); break;      // خواروبار: کِرم
            case 1: wall = new Color(0.45f, 0.5f, 0.6f); break;        // الکترونیک: فولادی
            case 2: wall = new Color(0.68f, 0.52f, 0.48f); break;      // جواهری: رز
            case 3: wall = new Color(0.82f, 0.8f, 0.74f); break;       // بانک: سنگ
            case 4: wall = new Color(0.27f, 0.21f, 0.32f); break;      // بلک‌مارکت: بنفش تیره
            default: wall = rng.Next(0, 2) == 0 ? new Color(0.58f, 0.3f, 0.25f) : new Color(0.7f, 0.65f, 0.55f); break;  // آجری / بژ
        }
        Color trim = Color.Lerp(wall, Color.white, 0.4f);
        Color dark = Color.Lerp(wall, Color.black, 0.5f);

        float wallTile;
        var wallMat = WallMat(role, rng, out wallTile);
        TexBox(ctr + new Vector3(0f, h / 2f, 0f), new Vector3(2f * half, h, 2f * half), wallMat, wallTile, true);
        Box(ctr + new Vector3(0f, 0.35f, 0f), new Vector3(2f * half + 0.3f, 0.7f, 2f * half + 0.3f), dark);
        for (int k = 1; k < floors; k++)
            Box(ctr + new Vector3(0f, 3.6f * k, 0f), new Vector3(2f * half + 0.2f, 0.16f, 2f * half + 0.2f), trim);
        Box(ctr + new Vector3(0f, h + 0.2f, 0f), new Vector3(2f * half + 0.5f, 0.4f, 2f * half + 0.5f), trim);
        // لوله‌ی ناودان در دو گوشه
        Cyl(ctr + new Vector3(half + 0.1f, h / 2f, half + 0.1f), new Vector3(0.14f, h / 2f, 0.14f), dark);
        Cyl(ctr + new Vector3(-half - 0.1f, h / 2f, half + 0.1f), new Vector3(0.14f, h / 2f, 0.14f), dark);

        bool shop = role <= 4;
        BuildWindows(ctr, half, floors, shop, rng);
        BuildRoof(ctr, h, role, rng, dark);
        if (shop) BuildStorefront(ctr, half, role, wall, trim, dark);
        else BuildApartmentFront(ctr, half, floors, trim, dark);
    }

    void BuildWindows(Vector3 ctr, float half, int floors, bool shopSouth, System.Random rng)
    {
        // 0 جنوب (سمت در)، 1 شمال، 2 شرق، 3 غرب
        for (int face = 0; face < 4; face++)
        {
            Vector3 n = face == 0 ? new Vector3(0f, 0f, -1f) : face == 1 ? new Vector3(0f, 0f, 1f) : face == 2 ? new Vector3(1f, 0f, 0f) : new Vector3(-1f, 0f, 0f);
            Vector3 t = face < 2 ? new Vector3(1f, 0f, 0f) : new Vector3(0f, 0f, 1f);
            float[] offs = face < 2 ? new[] { -4.2f, -1.4f, 1.4f, 4.2f } : new[] { -3.6f, 0f, 3.6f };
            Vector3 size = face < 2 ? new Vector3(1.5f, 1.7f, 0.12f) : new Vector3(0.12f, 1.7f, 1.5f);
            Vector3 fsize = face < 2 ? new Vector3(1.8f, 2.0f, 0.08f) : new Vector3(0.08f, 2.0f, 1.8f);

            for (int k = (face == 0 && shopSouth) ? 1 : 0; k < floors; k++)
                foreach (float o in offs)
                {
                    float y = 3.6f * k + 2.1f;
                    Color glass = rng.NextDouble() < 0.4 ? LitWindow : new Color(0.15f + (float)rng.NextDouble() * 0.06f, 0.2f, 0.28f);
                    Vector3 pos = ctr + n * (half + 0.03f) + t * o + new Vector3(0f, y, 0f);
                    Box(pos, size, glass);
                    Box(ctr + n * (half + 0.01f) + t * o + new Vector3(0f, y, 0f), fsize, new Color(0.1f, 0.1f, 0.12f));
                }
        }
    }

    void BuildRoof(Vector3 ctr, float h, int role, System.Random rng, Color dark)
    {
        Color metal = new Color(0.55f, 0.57f, 0.6f);
        int units = 1 + rng.Next(0, 3);
        for (int i = 0; i < units; i++)
        {
            Vector3 p = ctr + new Vector3(-3.5f + (float)rng.NextDouble() * 7f, h + 0.8f, -3.5f + (float)rng.NextDouble() * 7f);
            Box(p, new Vector3(1.4f, 0.8f, 1.1f), metal);
            Cyl(p + new Vector3(0f, 0.45f, 0f), new Vector3(0.7f, 0.04f, 0.7f), dark);
        }
        if (rng.Next(0, 2) == 0)
        {   // مخزن آب
            Vector3 p = ctr + new Vector3(3f, h + 1.4f, 3f);
            Cyl(p, new Vector3(1.6f, 0.9f, 1.6f), new Color(0.45f, 0.32f, 0.22f));
            Box(p + new Vector3(0f, -1.2f, 0f), new Vector3(0.2f, 0.6f, 1.4f), dark);
        }
        if (rng.Next(0, 2) == 0 || role == 4)
        {   // آنتن
            Vector3 p = ctr + new Vector3(-3f, h + 1.8f, 3.5f);
            Cyl(p, new Vector3(0.05f, 1.6f, 0.05f), dark);
            Box(p + new Vector3(0f, 0.9f, 0f), new Vector3(1.2f, 0.05f, 0.05f), dark);
        }
        Box(ctr + new Vector3(-4.2f, h + 0.9f, -4.2f), new Vector3(1.6f, 1.4f, 1.6f), Color.Lerp(dark, Color.gray, 0.4f));  // اتاقک راه‌پله
    }

    // ویترین، درِ ورودی، سایبان راه‌راه، تابلو، پله و گلدان
    void BuildStorefront(Vector3 ctr, float half, int role, Color wall, Color trim, Color dark)
    {
        Color accent = role == 4 ? MarketColor : ShopColors[role];
        float zf = ctr.z - half - 0.04f;      // سطح نمای جنوبی
        Color glass = Color.Lerp(new Color(0.12f, 0.17f, 0.24f), accent, 0.18f);

        // در
        Box(new Vector3(ctr.x, 1.45f, zf), new Vector3(2.1f, 2.9f, 0.18f), trim);
        Box(new Vector3(ctr.x, 1.25f, zf - 0.06f), new Vector3(1.6f, 2.5f, 0.12f), new Color(0.2f, 0.14f, 0.1f));
        Box(new Vector3(ctr.x + 0.55f, 1.2f, zf - 0.14f), new Vector3(0.08f, 0.08f, 0.06f), new Color(0.9f, 0.8f, 0.3f));   // دستگیره

        // ویترین‌ها
        foreach (float sx in new[] { -3.5f, 3.5f })
        {
            Box(new Vector3(ctr.x + sx, 1.75f, zf), new Vector3(3.5f, 2.5f, 0.12f), dark);
            Box(new Vector3(ctr.x + sx, 1.75f, zf - 0.05f), new Vector3(3.2f, 2.2f, 0.1f), glass);
            Box(new Vector3(ctr.x + sx, 0.45f, zf - 0.06f), new Vector3(3.5f, 0.5f, 0.14f), trim);   // زیرپنجره
        }

        // سایبان راه‌راه
        for (int i = 0; i < 6; i++)
        {
            var s = Box(new Vector3(ctr.x - 3.75f + i * 1.5f, 3.15f, zf - 0.85f), new Vector3(1.5f, 0.1f, 1.8f), i % 2 == 0 ? accent : Color.white);
            s.transform.localRotation = Quaternion.Euler(-14f, 0f, 0f);
        }

        // تابلو
        Box(new Vector3(ctr.x, 4.55f, zf - 0.1f), new Vector3(7.4f, 1.25f, 0.25f), dark);
        Box(new Vector3(ctr.x, 4.55f, zf - 0.25f), new Vector3(7f, 0.9f, 0.1f), accent);

        // پله و گلدان
        Box(new Vector3(ctr.x, 0.1f, zf - 0.8f), new Vector3(3.2f, 0.2f, 1.4f), new Color(0.5f, 0.5f, 0.52f));
        foreach (float sx in new[] { -2.2f, 2.2f })
        {
            Cyl(new Vector3(ctr.x + sx, 0.4f, zf - 0.9f), new Vector3(0.6f, 0.4f, 0.6f), new Color(0.55f, 0.3f, 0.2f));
            Ball(new Vector3(ctr.x + sx, 1.0f, zf - 0.9f), Vector3.one * 0.8f, new Color(0.2f, 0.5f, 0.25f));
        }
        foreach (float sx in new[] { -1.4f, 1.4f })
            Ball(new Vector3(ctr.x + sx, 3.5f, zf - 0.3f), Vector3.one * 0.25f, new Color(1f, 0.9f, 0.6f));   // چراغ بالای در

        if (role == 4)
        {   // نئون بنفش دور تابلوی بلک‌مارکت
            Color neon = new Color(1f, 0.35f, 1f);
            Box(new Vector3(ctr.x, 5.2f, zf - 0.3f), new Vector3(7.4f, 0.08f, 0.08f), neon);
            Box(new Vector3(ctr.x, 3.9f, zf - 0.3f), new Vector3(7.4f, 0.08f, 0.08f), neon);
        }
        if (role == 3)
        {   // ستون‌های بانک و سردر
            foreach (float sx in new[] { -4.8f, -2.4f, 2.4f, 4.8f })
                Cyl(new Vector3(ctr.x + sx, 2.0f, zf - 1.6f), new Vector3(0.7f, 2.0f, 0.7f), new Color(0.9f, 0.88f, 0.82f));
            Box(new Vector3(ctr.x, 4.15f, zf - 1.6f), new Vector3(11.2f, 0.4f, 1.1f), new Color(0.9f, 0.88f, 0.82f));
        }
    }

    // ساختمان معمولی: در ساده، سایبان کوچک، بالکن
    void BuildApartmentFront(Vector3 ctr, float half, int floors, Color trim, Color dark)
    {
        float zf = ctr.z - half - 0.04f;
        Box(new Vector3(ctr.x, 1.3f, zf), new Vector3(1.8f, 2.6f, 0.2f), new Color(0.25f, 0.17f, 0.12f));
        Box(new Vector3(ctr.x, 2.8f, zf - 0.5f), new Vector3(3f, 0.12f, 1.2f), dark);
        Box(new Vector3(ctr.x, 0.1f, zf - 0.7f), new Vector3(2.4f, 0.2f, 1.2f), new Color(0.5f, 0.5f, 0.52f));
        for (int k = 1; k < floors; k++)
            foreach (float sx in new[] { -2.8f, 2.8f })
            {
                Box(new Vector3(ctr.x + sx, 3.6f * k, zf - 0.5f), new Vector3(2.6f, 0.15f, 1.0f), trim);
                Box(new Vector3(ctr.x + sx, 3.6f * k + 0.5f, zf - 0.98f), new Vector3(2.6f, 0.9f, 0.05f), dark);
            }
    }

    // ---------------------------------------------------------------- Base
    void BuildBase()
    {
        ResetRoot();
        Color wall = new Color(0.5f, 0.45f, 0.42f);
        TexBox(new Vector3(0f, -0.1f, 0f), new Vector3(24f, 0.2f, 18f), TexMat("wood", Color.white, 0.2f), 2.4f, true);
        var baseWall = TexMat("plaster", new Color(0.95f, 0.88f, 0.8f), 0.06f);
        TexBox(new Vector3(0f, 2f, 9.25f), new Vector3(24.5f, 4f, 0.5f), baseWall, 4f, true);
        TexBox(new Vector3(0f, 2f, -9.25f), new Vector3(24.5f, 4f, 0.5f), baseWall, 4f, true);
        TexBox(new Vector3(-12.25f, 2f, 0f), new Vector3(0.5f, 4f, 18f), baseWall, 4f, true);
        TexBox(new Vector3(12.25f, 2f, 0f), new Vector3(0.5f, 4f, 18f), baseWall, 4f, true);

        // قالیچه
        Box(new Vector3(-3f, 0.02f, -3f), new Vector3(7f, 0.03f, 4.5f), new Color(0.45f, 0.12f, 0.12f));
        Box(new Vector3(-3f, 0.035f, -3f), new Vector3(6.2f, 0.03f, 3.7f), new Color(0.6f, 0.2f, 0.18f));

        // کاناپه و تلویزیون
        Color sofa = new Color(0.25f, 0.28f, 0.38f);
        Box(new Vector3(-3f, 0.35f, -5.3f), new Vector3(3.2f, 0.7f, 1.1f), sofa, true);
        Box(new Vector3(-3f, 0.9f, -5.75f), new Vector3(3.2f, 0.8f, 0.25f), sofa);
        Box(new Vector3(-4.7f, 0.55f, -5.3f), new Vector3(0.3f, 0.5f, 1.1f), sofa * 0.9f);
        Box(new Vector3(-1.3f, 0.55f, -5.3f), new Vector3(0.3f, 0.5f, 1.1f), sofa * 0.9f);
        Box(new Vector3(-3f, 0.25f, -3.2f), new Vector3(1.6f, 0.5f, 0.9f), new Color(0.35f, 0.25f, 0.18f), true);   // میز
        Box(new Vector3(-3f, 1.7f, -8.9f), new Vector3(3f, 1.7f, 0.15f), new Color(0.05f, 0.05f, 0.07f));          // تلویزیون
        Box(new Vector3(-3f, 1.7f, -8.82f), new Vector3(2.7f, 1.4f, 0.05f), new Color(0.2f, 0.45f, 0.7f));

        // پنجره‌ها و پوستر
        Box(new Vector3(-11.95f, 2.1f, -2f), new Vector3(0.1f, 1.8f, 3f), new Color(1f, 0.8f, 0.5f));
        Box(new Vector3(-11.95f, 2.1f, 3.5f), new Vector3(0.1f, 1.8f, 3f), new Color(1f, 0.8f, 0.5f));
        Box(new Vector3(-11.9f, 2.1f, -2f), new Vector3(0.08f, 0.1f, 3.1f), new Color(0.1f, 0.1f, 0.12f));
        Box(new Vector3(-3f, 2.6f, 8.95f), new Vector3(1.6f, 2f, 0.05f), new Color(0.8f, 0.2f, 0.3f));
        Box(new Vector3(4f, 2.6f, 8.95f), new Vector3(1.6f, 2f, 0.05f), new Color(0.2f, 0.5f, 0.8f));

        // گلدان
        foreach (var p in new[] { new Vector3(-10.5f, 0f, 7.8f), new Vector3(10.5f, 0f, 7.8f), new Vector3(10.5f, 0f, -7.8f) })
        {
            Cyl(p + new Vector3(0f, 0.4f, 0f), new Vector3(0.7f, 0.4f, 0.7f), new Color(0.55f, 0.3f, 0.2f));
            Ball(p + new Vector3(0f, 1.2f, 0f), Vector3.one * 1.1f, new Color(0.2f, 0.5f, 0.25f));
        }

        // میز فروشنده‌ی اسلحه (قفسه‌ی اسلحه پشتش) و کمد جلیقه
        MakeCounter(new Vector3(-6f, 0f, 6.5f), new Color(0.8f, 0.2f, 0.2f), "WEAPON DEALER", SpotType.WeaponDealer);
        for (int i = 0; i < 5; i++)
        {
            Box(new Vector3(-8f + i * 1.0f, 2.2f, 8.9f), new Vector3(0.85f, 0.12f, 0.1f), new Color(0.12f, 0.12f, 0.14f));
            Box(new Vector3(-8f + i * 1.0f, 2.1f, 8.9f), new Vector3(0.12f, 0.25f, 0.1f), new Color(0.12f, 0.12f, 0.14f));
        }
        Box(new Vector3(-6f, 1.8f, 8.95f), new Vector3(5.4f, 0.1f, 0.3f), new Color(0.3f, 0.2f, 0.12f));
        MakeCounter(new Vector3(0f, 0f, 6.5f), new Color(0.25f, 0.45f, 0.95f), "ARMOR LOCKER", SpotType.ArmorLocker);
        for (int i = 0; i < 3; i++)
            Box(new Vector3(-1f + i * 1.0f, 2.0f, 8.85f), new Vector3(0.7f, 0.9f, 0.25f), new Color(0.2f + 0.1f * i, 0.28f, 0.22f));

        // در گاراژ (شرق) با خطوط و علامت هشدار
        Box(new Vector3(11.9f, 1.8f, 0f), new Vector3(0.3f, 3.6f, 7f), new Color(0.15f, 0.5f, 0.25f));
        for (int i = 0; i < 6; i++)
            Box(new Vector3(11.7f, 0.5f + i * 0.6f, 0f), new Vector3(0.1f, 0.06f, 6.8f), new Color(0.3f, 0.7f, 0.4f));
        for (int i = 0; i < 6; i++)
            Box(new Vector3(8.2f, 0.012f, -3f + i * 1.2f), new Vector3(0.6f, 0.02f, 0.6f), i % 2 == 0 ? new Color(0.95f, 0.8f, 0.1f) : new Color(0.1f, 0.1f, 0.1f));
        MakeSpot(SpotType.Garage, new Vector3(9.3f, 0f, 0f), new Color(0.2f, 1f, 0.4f), "GARAGE -> CITY");

        // جعبه‌ها
        Box(new Vector3(-9f, 0.6f, -6f), new Vector3(1.6f, 1.2f, 1.6f), new Color(0.5f, 0.35f, 0.2f), true);
        Box(new Vector3(-7.2f, 0.4f, -6.4f), new Vector3(1.1f, 0.8f, 1.1f), new Color(0.55f, 0.4f, 0.22f), true);
        Box(new Vector3(8f, 0.6f, -6.5f), new Vector3(1.6f, 1.2f, 1.6f), new Color(0.5f, 0.35f, 0.2f), true);
        Box(new Vector3(8f, 1.5f, -6.5f), new Vector3(1.0f, 0.6f, 1.0f), new Color(0.55f, 0.4f, 0.22f));

        CombineScenery();
        CreatePlayer(new Vector3(0f, 0.1f, 0f), 0f);
    }

    void MakeCounter(Vector3 pos, Color c, string label, SpotType type)
    {
        Box(pos + Vector3.up * 0.55f, new Vector3(3.5f, 1.1f, 1.2f), c, true);
        Box(pos + Vector3.up * 1.13f, new Vector3(3.7f, 0.1f, 1.4f), new Color(0.85f, 0.85f, 0.85f));
        MakeSpot(type, pos + new Vector3(0f, 0f, -2.2f), c, label);
    }

    // ---------------------------------------------------------------- Characters
    void CreatePlayer(Vector3 pos, float yaw)
    {
        var p = new GameObject("Player");
        p.transform.SetParent(root, false);
        p.transform.position = pos;

        var look = new HumanLook
        {
            skin = SkinTones[1], shirt = new Color(0.12f, 0.12f, 0.15f), pants = new Color(0.16f, 0.22f, 0.36f),
            shoes = new Color(0.9f, 0.9f, 0.9f), hair = new Color(0.1f, 0.08f, 0.06f), mask = true, bag = true
        };
        BuildHuman(p.transform, look);

        var cc = p.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.radius = 0.4f; cc.center = new Vector3(0f, 0.9f, 0f); cc.stepOffset = 0.3f;
        var pc = p.AddComponent<PlayerController>();
        pc.Init(this, yaw);
        Player = pc;
    }

    void CreateCop(Vector2Int cell)
    {
        var r = new GameObject("Cop");
        r.transform.SetParent(root, false);
        r.transform.position = CellToWorld(cell);

        var look = new HumanLook
        {
            skin = SkinTones[Random.Range(0, SkinTones.Length)], shirt = new Color(0.1f, 0.2f, 0.5f), pants = new Color(0.07f, 0.09f, 0.18f),
            shoes = Color.black, hair = new Color(0.15f, 0.1f, 0.07f), cap = true, glasses = true, badge = true
        };
        var hu = BuildHuman(r.transform, look);
        hu.gun.localScale = new Vector3(0.06f, 0.09f, 0.3f);
        hu.ShowGun(0);

        var col = r.AddComponent<CapsuleCollider>();
        col.height = 1.85f; col.radius = 0.35f; col.center = new Vector3(0f, 0.92f, 0f);
        var rc = r.AddComponent<RobotCop>();
        rc.Init(this, Player, hu);
        cops.Add(rc);
    }
}
