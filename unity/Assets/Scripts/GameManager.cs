using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum GameState { Menu, Base, City, Dead }
public enum UiPanel { None, Weapons, Armor }
public enum SpotType { Shop, Market, Exit, Garage, WeaponDealer, ArmorLocker }

public class Spot
{
    public SpotType type;
    public Vector3 pos;
    public string label;
    public int shopType;        // 0 خواروبار، 1 الکترونیک، 2 جواهری، 3 بانک
    public float duration;      // ثانیه‌ی نگه‌داشتن E برای سرقت
    public bool done, alarmed;
    public Color color;
    public GameObject pad, beam;
}

// منطق اصلی بازی. بقیه‌ی کد تو GameManager.World.cs و GameManager.UI.cs
public partial class GameManager : MonoBehaviour
{
    public const float Cell = 4f;
    public const int CW = 25, CH = 25;

    public GameState State { get; private set; } = GameState.Menu;
    public UiPanel CurrentPanel { get; private set; } = UiPanel.None;
    public int Wanted { get; private set; }
    public PlayerController Player { get; private set; }
    public Camera Cam => cam;
    public bool Running => State == GameState.City && !paused;
    public bool InputBlocked => !(State == GameState.Base || State == GameState.City) || CurrentPanel != UiPanel.None || paused;
    public float LastShotTime { get; private set; } = -10f;
    public Vector3 LastShotPos { get; private set; }

    Camera cam;
    Shader shader;
    Shader lineShader;
    Transform root;
    readonly List<Spot> spots = new List<Spot>();
    readonly List<RobotCop> cops = new List<RobotCop>();
    readonly List<Vector2Int> walkCells = new List<Vector2Int>();
    readonly List<Vector2Int> buildingMins = new List<Vector2Int>();
    bool[,] blocked;

    public const int BagSize = 8;
    static readonly int[] ItemsPerRob = { 1, 2, 2, 3 };    // خواروبار، الکترونیک، جواهری، بانک
    readonly List<int> bag = new List<int>();               // آیتم‌های داخل کیف (id آیتم)
    int runCash;
    float marketMult = 1f;
    Spot robbingSpot;
    float robProgress, calmTimer, lastSeenTime;
    bool exitWarned, paused;
    string hint = "", deathMsg = "", fatalMsg = "";

    class ToastMsg { public string text; public float until; }
    readonly List<ToastMsg> toasts = new List<ToastMsg>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (GameObject.Find("GameManager") == null)
            new GameObject("GameManager").AddComponent<GameManager>();
    }

    int BagValue()
    {
        int v = 0;
        foreach (int id in bag) v += NativeCore.ItemValue[id];
        return v;
    }

    void Awake()
    {
        gameObject.AddComponent<GameAudio>();
        BuildCityGrid();
        bool ok = NativeCore.Init(CityMapString(), CW, CH, Path.Combine(Application.persistentDataPath, "profile.txt"));
        if (!ok) fatalMsg = "C++ core not loaded (" + NativeCore.Error + ").\nRun native/build_windows.bat, then restart Unity.";
        SetupCameraAndLight();
        if (ok) BuildBase();   // پشت منوی اصلی دیده میشه
    }

    // ---------------------------------------------------------------- Flow
    void StartBase(string msg = null)
    {
        BuildBase();
        State = GameState.Base;
        CurrentPanel = UiPanel.None;
        paused = false;
        Wanted = 0;
        if (msg != null) Toast(msg);
    }

    void StartCity()
    {
        State = GameState.City;
        Wanted = 0; calmTimer = 0f;
        bag.Clear(); runCash = 0; exitWarned = false;
        CurrentPanel = UiPanel.None;
        BuildCity(Random.Range(0, 100000));
        Toast("Rob shops, sell the loot at the BLACK MARKET (purple), then reach the EXIT.");
        Toast("Black market rate today: x" + marketMult.ToString("0.00"));
    }

    public void PlayerDied()
    {
        if (State != GameState.City) return;
        State = GameState.Dead;
        deathMsg = "You lost $" + BagValue() + " of loot (" + bag.Count + " items) and $" + runCash + " of unbanked cash.";
        bag.Clear(); runCash = 0;
        robbingSpot = null; robProgress = 0f; hint = "";
    }

    // ---------------------------------------------------------------- Update
    void Update()
    {
        UpdateCursor();
        UpdateAudio();
        if (!NativeCore.Available) return;
        if (Player != null && Player.Human != null) Player.Human.SetBag(bag.Count / (float)BagSize);
        if (State == GameState.Menu) return;

        if (State == GameState.Dead)
        {
            if (GameInput.InteractDown()) StartBase("You woke up at your base.");
            return;
        }

        if (GameInput.EscDown())
        {
            if (CurrentPanel != UiPanel.None) CurrentPanel = UiPanel.None;
            else paused = !paused;
        }
        toasts.RemoveAll(t => Time.time > t.until);
        if (paused || CurrentPanel != UiPanel.None) return;

        UpdateSpots();
        if (State == GameState.City) UpdateWanted();
    }

    void UpdateAudio()
    {
        var a = GameAudio.I;
        if (a == null) return;
        if (GameInput.MuteDown()) a.ToggleMusic();
        float b = 0f, c = 0f;
        switch (State)
        {
            case GameState.Menu:
            case GameState.Base: b = 0.15f; break;
            case GameState.City: c = Wanted >= 3 ? 0.08f : 0.13f; break;
            case GameState.Dead: b = 0.05f; break;
        }
        a.SetMood(b, c);
    }

    void UpdateCursor()
    {
        bool lockIt = (State == GameState.City || State == GameState.Base) && CurrentPanel == UiPanel.None && !paused;
        Cursor.lockState = lockIt ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !lockIt;
    }

    void UpdateSpots()
    {
        hint = "";
        if (Player == null) return;

        Spot near = null;
        float best = 3f * 3f;
        foreach (var s in spots)
        {
            if (s.done) continue;
            float d = Flat(s.pos - Player.transform.position).sqrMagnitude;
            if (d < best) { best = d; near = s; }
        }
        if (near == null || near.type != SpotType.Shop) CancelRob();
        if (near == null) return;

        switch (near.type)
        {
            case SpotType.Shop:
                if (bag.Count >= BagSize)
                {
                    hint = "Your bag is full! Sell the loot at the BLACK MARKET first.";
                    CancelRob();
                    break;
                }
                hint = "Hold E to rob " + near.label;
                if (GameInput.InteractHeld())
                {
                    if (robbingSpot != near)
                    {
                        robbingSpot = near; robProgress = 0f;
                        if (!near.alarmed) { near.alarmed = true; RaiseWanted(near.shopType >= 3 ? 2 : 1); }
                    }
                    robProgress += Time.deltaTime;
                    if (robProgress >= near.duration) CompleteRob(near);
                }
                else CancelRob();
                break;

            case SpotType.Market:
                hint = bag.Count > 0 ? "Press E to sell " + bag.Count + " item(s)  ($" + BagValue() + " x" + marketMult.ToString("0.00") + ")" : "Black Market - your bag is empty";
                if (GameInput.InteractDown()) SellLoot();
                break;

            case SpotType.Exit:
                hint = "Press E to leave the city and go to your base";
                if (GameInput.InteractDown()) TryExit();
                break;

            case SpotType.Garage:
                hint = "Press E to head out to the city";
                if (GameInput.InteractDown()) StartCity();
                break;

            case SpotType.WeaponDealer:
                hint = "Press E - Weapon Dealer";
                if (GameInput.InteractDown()) CurrentPanel = UiPanel.Weapons;
                break;

            case SpotType.ArmorLocker:
                hint = "Press E - Armor Locker";
                if (GameInput.InteractDown()) CurrentPanel = UiPanel.Armor;
                break;
        }
    }

    void CancelRob() { robbingSpot = null; robProgress = 0f; }

    public float RobProgress01 => robbingSpot != null ? Mathf.Clamp01(robProgress / robbingSpot.duration) : 0f;

    void CompleteRob(Spot s)
    {
        int want = ItemsPerRob[Mathf.Clamp(s.shopType, 0, 3)];
        var names = new List<string>();
        bool rare = false;
        for (int k = 0; k < want && bag.Count < BagSize; k++)
        {
            int id = NativeCore.LootPick(s.shopType, Random.Range(0, 10000));
            if (id < 0) continue;
            bag.Add(id);
            bool sp = NativeCore.ItemSpecial[id];
            rare |= sp;
            names.Add((sp ? "[RARE] " : "") + NativeCore.ItemNames[id] + " ($" + NativeCore.ItemValue[id] + ")");
        }
        s.done = true;
        if (s.pad != null) s.pad.GetComponent<Renderer>().material.color = Color.gray;
        if (s.beam != null) Destroy(s.beam);
        NativeCore.ProfileAdd(NativeCore.Robberies, 1);
        Toast((rare ? "RARE FIND!  " : "") + s.label + ": " + string.Join(", ", names.ToArray()));
        if (GameAudio.I != null) GameAudio.I.Pickup();
        CancelRob();
    }

    void SellLoot()
    {
        if (bag.Count == 0) { Toast("Your bag is empty."); return; }
        int cash = NativeCore.SellPrice(BagValue(), Mathf.RoundToInt(marketMult * 100f));
        runCash += cash;
        Toast("Sold " + bag.Count + " item(s) for $" + cash + ". Now get to the EXIT!");
        bag.Clear();
        if (GameAudio.I != null) GameAudio.I.Cash();
    }

    void TryExit()
    {
        if (bag.Count > 0 && !exitWarned)
        {
            exitWarned = true;
            Toast("Your bag still holds $" + BagValue() + " of unsold loot! Press E again to leave without it.");
            return;
        }
        int cash = runCash;
        NativeCore.BankRun(cash);
        StartBase("Made it home! Banked $" + cash + ".");
    }

    // ---------------------------------------------------------------- Heat & police
    void RaiseWanted(int n)
    {
        int old = Wanted;
        Wanted = Mathf.Min(5, Wanted + n);
        calmTimer = 0f;
        if (Wanted > old)
        {
            Toast("ALARM! Wanted level " + Wanted);
            if (GameAudio.I != null) GameAudio.I.Alarm();
        }
        SpawnCops(n);
    }

    void UpdateWanted()
    {
        if (Time.time - lastSeenTime < 1f) calmTimer = 0f; else calmTimer += Time.deltaTime;
        if (Wanted > 0 && calmTimer > 12f)
        {
            Wanted--; calmTimer = 0f;
            Toast("The heat is dropping... wanted level " + Wanted);
        }
    }

    public void NotifySeen() { lastSeenTime = Time.time; }

    public void CopKilled(RobotCop c)
    {
        cops.Remove(c);
        NativeCore.ProfileAdd(NativeCore.Kills, 1);
        if (Wanted < 5) RaiseWanted(1);
        else SpawnCops(1);
    }

    void SpawnCops(int n)
    {
        if (State != GameState.City || Player == null) return;
        for (int i = 0; i < n && cops.Count < 10; i++)
            for (int tries = 0; tries < 30; tries++)
            {
                var c = walkCells[Random.Range(0, walkCells.Count)];
                if (Flat(CellToWorld(c) - Player.transform.position).magnitude < 25f) continue;
                CreateCop(c);
                break;
            }
    }

    public void OnPlayerShot(Vector3 pos) { LastShotTime = Time.time; LastShotPos = pos; }

    // ---------------------------------------------------------------- Shopping (base)
    void BuyWeapon(int i)
    {
        int r = NativeCore.BuyWeapon(i);
        if (r == 1) { Toast("Bought " + NativeCore.WeaponNames[i] + "! Press " + (i + 1) + " in the field to equip it."); if (GameAudio.I != null) GameAudio.I.Coin(); }
        else if (r == 0) Toast("Not enough money.");
    }

    void BuyVest(int level)
    {
        int r = NativeCore.BuyVest(level);
        if (r == 1) { Player.RefreshLoadout(); Toast("Bought " + NativeCore.VestNames[level] + "."); if (GameAudio.I != null) GameAudio.I.Coin(); }
        else if (r == 0) Toast("Not enough money.");
    }

    void Toast(string text) { toasts.Add(new ToastMsg { text = text, until = Time.time + 5f }); if (toasts.Count > 5) toasts.RemoveAt(0); }

    // ---------------------------------------------------------------- Helpers
    public static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    public Vector3 CellToWorld(Vector2Int c) => new Vector3(c.x * Cell, 0f, -c.y * Cell);
    public Vector2Int WorldToCell(Vector3 p) => new Vector2Int(Mathf.RoundToInt(p.x / Cell), Mathf.RoundToInt(-p.z / Cell));
    public Vector2Int RandomWalkCell() => walkCells[Random.Range(0, walkCells.Count)];
    public List<Vector2Int> FindPath(Vector2Int a, Vector2Int b) => NativeCore.FindPath(a, b);

    static readonly RaycastHit[] hitBuf = new RaycastHit[32];

    // نزدیک‌ترین برخورد، به‌جز collider های خود تیرانداز
    public static bool RayIgnore(Transform ignore, Vector3 origin, Vector3 dir, float dist, out RaycastHit best)
    {
        best = default;
        int n = Physics.RaycastNonAlloc(origin, dir, hitBuf, dist, ~0, QueryTriggerInteraction.Ignore);
        float bd = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            var h = hitBuf[i];
            if (ignore != null && h.collider.transform.IsChildOf(ignore)) continue;
            if (h.distance < bd) { bd = h.distance; best = h; found = true; }
        }
        return found;
    }

    public void Tracer(Vector3 a, Vector3 b, Color c)
    {
        if (lineShader == null) lineShader = Shader.Find("Sprites/Default");
        var go = new GameObject("Tracer");
        var lr = go.AddComponent<LineRenderer>();
        lr.material = new Material(lineShader);
        lr.startColor = c; lr.endColor = new Color(c.r, c.g, c.b, 0.2f);
        lr.startWidth = 0.05f; lr.endWidth = 0.02f;
        lr.positionCount = 2;
        lr.SetPosition(0, a); lr.SetPosition(1, b);
        Destroy(go, 0.06f);
    }
}
