using UnityEngine;

// رابط کاربری (IMGUI) - بدون نیاز به Canvas یا TextMeshPro
public partial class GameManager
{
    GUIStyle sLabel, sBig, sCenter, sRight, sSmall, sBtn;

    void InitStyles()
    {
        if (sLabel != null) return;
        sLabel = new GUIStyle(GUI.skin.label) { fontSize = 20 };
        sLabel.normal.textColor = Color.white;
        sBig = new GUIStyle(sLabel) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        sCenter = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter };
        sRight = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleRight };
        sSmall = new GUIStyle(sLabel) { fontSize = 15 };
        sBtn = new GUIStyle(GUI.skin.button) { fontSize = 20 };
    }

    static void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    void Text(Rect r, string t, GUIStyle st, Color c)
    {
        var old = st.normal.textColor;
        st.normal.textColor = c;
        GUI.Label(r, t, st);
        st.normal.textColor = old;
    }

    void OnGUI()
    {
        float s = Screen.height / 720f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float W = Screen.width / s, H = 720f;
        InitStyles();

        if (State == GameState.Menu || !NativeCore.Available) { DrawMenu(W, H); return; }

        DrawWorldLabels(s);
        DrawHud(W, H);
        if (State == GameState.Dead) DrawDead(W, H);
        if (CurrentPanel != UiPanel.None) DrawPanel(W, H);
        if (paused)
        {
            Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.5f));
            GUI.Label(new Rect(0, H / 2 - 40, W, 80), "PAUSED  -  Esc to resume", sBig);
        }
    }

    // ---------------------------------------------------------------- Menu
    void DrawMenu(float W, float H)
    {
        Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.6f));
        float bw = 620, x = (W - bw) / 2, y = 60;
        GUI.Label(new Rect(x, y, bw, 60), "ROBOT HEIST", sBig); y += 64;
        GUI.Label(new Rect(x, y, bw, 30), "Rob shops. Sell the loot. Escape the robot cops.", sCenter); y += 50;

        if (!NativeCore.Available)
        {
            Text(new Rect(x, y, bw, 100), fatalMsg, sCenter, new Color(1f, 0.4f, 0.4f));
            return;
        }

        string[] lines =
        {
            "WASD  move          Mouse  look / aim",
            "Left mouse  shoot     R  reload     Shift  sprint",
            "1-5  switch weapon     E  interact (hold E to rob)",
            "M  music on/off     Esc  pause",
            "",
            "1. Rob shops (hold E at the glowing door)",
            "2. Your bag holds 6 items - sell them at the purple BLACK MARKET",
            "3. Reach the green EXIT at the east edge -> your base",
            "4. Spend your cash on weapons and vests",
            "If you die, you lose the loot and unbanked cash."
        };
        foreach (var l in lines) { GUI.Label(new Rect(x, y, bw, 26), l, sCenter); y += 26; }
        y += 14;

        if (ModelLoader.Status.Length > 0) Text(new Rect(x - 100, y - 4, bw + 200, 22), "Models: " + ModelLoader.Status, sSmall, new Color(0.6f, 0.9f, 0.6f));
        y += 22;
        GUI.Label(new Rect(x, y, bw, 28),
            "Bank $" + NativeCore.Profile(NativeCore.Money) + "   Runs " + NativeCore.Profile(NativeCore.Runs) +
            "   Robberies " + NativeCore.Profile(NativeCore.Robberies) + "   Cops down " + NativeCore.Profile(NativeCore.Kills), sCenter);
        y += 44;
        if (GUI.Button(new Rect(x + 180, y, bw - 360, 52), "START", sBtn)) StartBase();
    }

    // ---------------------------------------------------------------- HUD
    void DrawHud(float W, float H)
    {
        var p = Player;
        if (p == null) return;

        // سلامتی و جلیقه
        Fill(new Rect(20, H - 56, 260, 20), new Color(0, 0, 0, 0.55f));
        Fill(new Rect(20, H - 56, 260 * Mathf.Clamp01(p.health / 100f), 20), new Color(0.85f, 0.15f, 0.15f));
        GUI.Label(new Rect(24, H - 59, 260, 24), "HP " + Mathf.CeilToInt(p.health), sSmall);
        if (p.maxArmor > 0f)
        {
            Fill(new Rect(20, H - 82, 260, 20), new Color(0, 0, 0, 0.55f));
            Fill(new Rect(20, H - 82, 260 * Mathf.Clamp01(p.armor / p.maxArmor), 20), new Color(0.25f, 0.5f, 1f));
            GUI.Label(new Rect(24, H - 85, 260, 24), "ARMOR " + Mathf.CeilToInt(p.armor), sSmall);
        }

        // اسلحه
        var w = NativeCore.Weapons[p.weapon];
        string ammo = p.Reloading ? "RELOADING..." : p.Ammo + " / " + (int)w.mag;
        GUI.Label(new Rect(W - 380, H - 92, 360, 30), NativeCore.WeaponNames[p.weapon] + "   " + ammo, sRight);
        for (int i = 0; i < NativeCore.WeaponCount; i++)
        {
            var r = new Rect(W - 20 - (NativeCore.WeaponCount - i) * 56, H - 56, 52, 30);
            bool owned = NativeCore.OwnsWeapon(i);
            Fill(r, i == p.weapon ? new Color(1f, 0.8f, 0.2f, 0.9f) : new Color(0, 0, 0, owned ? 0.6f : 0.3f));
            Text(r, (i + 1).ToString(), sCenter, i == p.weapon ? Color.black : (owned ? Color.white : Color.gray));
        }

        // پول
        GUI.Label(new Rect(20, 12, 500, 28), "Bank: $" + NativeCore.Profile(NativeCore.Money), sLabel);
        if (State == GameState.City)
        {
            Text(new Rect(20, 40, 500, 28), "Bag value: $" + BagValue(), sLabel, new Color(1f, 0.85f, 0.3f));
            Text(new Rect(20, 68, 500, 28), "Cash this run: $" + runCash, sLabel, new Color(0.5f, 1f, 0.6f));
        }

        // ستاره‌های تحت تعقیب
        if (State == GameState.City)
        {
            string stars = "";
            for (int i = 0; i < 5; i++) stars += i < Wanted ? "*" : "-";
            Text(new Rect(W / 2 - 150, 10, 300, 40), "WANTED " + stars, sCenter, Wanted > 0 ? new Color(1f, 0.3f, 0.3f) : Color.gray);
        }

        // نشانه‌گیر
        if (!InputBlocked)
        {
            float cx = W / 2, cy = H / 2;
            Color cc = new Color(1, 1, 1, 0.9f);
            Fill(new Rect(cx - 1, cy - 10, 2, 6), cc); Fill(new Rect(cx - 1, cy + 4, 2, 6), cc);
            Fill(new Rect(cx - 10, cy - 1, 6, 2), cc); Fill(new Rect(cx + 4, cy - 1, 6, 2), cc);
        }

        // راهنما و نوار سرقت
        if (hint.Length > 0 && !InputBlocked)
        {
            Fill(new Rect(W / 2 - 260, H - 170, 520, 36), new Color(0, 0, 0, 0.55f));
            GUI.Label(new Rect(W / 2 - 260, H - 170, 520, 36), hint, sCenter);
            if (robbingSpot != null)
            {
                Fill(new Rect(W / 2 - 200, H - 128, 400, 14), new Color(0, 0, 0, 0.6f));
                Fill(new Rect(W / 2 - 200, H - 128, 400 * RobProgress01, 14), new Color(1f, 0.8f, 0.2f));
            }
        }

        // پیام‌ها
        float ty = 120;
        foreach (var t in toasts)
        {
            Fill(new Rect(18, ty, 620, 26), new Color(0, 0, 0, 0.45f));
            GUI.Label(new Rect(24, ty, 610, 26), t.text, sSmall);
            ty += 28;
        }

        if (State == GameState.City) { DrawMinimap(W); DrawBag(W); }

        // فلش قرمز وقتی ضربه می‌خوری
        if (p.hurtTime > 0f) Fill(new Rect(0, 0, W, H), new Color(0.8f, 0f, 0f, Mathf.Clamp01(p.hurtTime * 1.2f)));
    }

    // لیست آیتم‌های داخل کیف (آیتم‌های کمیاب طلایی)
    void DrawBag(float W)
    {
        float x = W - 250f, y = 196f;
        Text(new Rect(x, y, 238, 24), "BAG " + bag.Count + "/" + BagSize + "   $" + BagValue(), sSmall, Color.white);
        y += 24f;
        foreach (int id in bag)
        {
            bool rare = NativeCore.ItemSpecial[id];
            Fill(new Rect(x, y, 238, 22), new Color(0f, 0f, 0f, 0.5f));
            Text(new Rect(x + 4, y, 232, 22), NativeCore.ItemNames[id] + "  $" + NativeCore.ItemValue[id], sSmall, rare ? new Color(1f, 0.8f, 0.2f) : new Color(0.9f, 0.9f, 0.9f));
            y += 24f;
        }
    }

    void DrawWorldLabels(float s)
    {
        if (cam == null || Player == null) return;
        foreach (var sp in spots)
        {
            if (sp.done) continue;
            if ((sp.pos - Player.transform.position).sqrMagnitude > 45f * 45f) continue;
            Vector3 sc = cam.WorldToScreenPoint(sp.pos + Vector3.up * 7f);
            if (sc.z <= 0f) continue;
            Text(new Rect(sc.x / s - 120, (Screen.height - sc.y) / s - 12, 240, 24), sp.label, sCenter, sp.color);
        }
    }

    void DrawMinimap(float W)
    {
        float size = 170f;
        var r = new Rect(W - size - 12, 12, size, size);
        Fill(r, new Color(0, 0, 0, 0.55f));
        foreach (var b in buildingMins)
            Fill(new Rect(r.x + b.x / (float)CW * size, r.y + b.y / (float)CH * size, 3f / CW * size, 3f / CH * size), new Color(0.35f, 0.35f, 0.4f, 0.95f));
        foreach (var sp in spots) if (!sp.done) Dot(r, sp.pos, sp.color, 7f);
        foreach (var c in cops) if (c != null) Dot(r, c.transform.position, Color.red, 5f);
        if (Player != null) Dot(r, Player.transform.position, Color.white, 7f);
    }

    void Dot(Rect map, Vector3 w, Color c, float sz)
    {
        float u = (w.x / Cell + 0.5f) / CW, v = (-w.z / Cell + 0.5f) / CH;
        Fill(new Rect(map.x + u * map.width - sz / 2f, map.y + v * map.height - sz / 2f, sz, sz), c);
    }

    // ---------------------------------------------------------------- Dead
    void DrawDead(float W, float H)
    {
        Fill(new Rect(0, 0, W, H), new Color(0.35f, 0f, 0f, 0.6f));
        GUI.Label(new Rect(0, H / 2 - 90, W, 70), "BUSTED", sBig);
        GUI.Label(new Rect(0, H / 2 - 20, W, 30), deathMsg, sCenter);
        GUI.Label(new Rect(0, H / 2 + 20, W, 30), "Press E to wake up at your base", sCenter);
    }

    // ---------------------------------------------------------------- Shop panels
    void DrawPanel(float W, float H)
    {
        float bw = 760, bh = CurrentPanel == UiPanel.Weapons ? 440 : 330;
        float x = (W - bw) / 2, y = (H - bh) / 2;
        Fill(new Rect(x, y, bw, bh), new Color(0.08f, 0.09f, 0.12f, 0.95f));
        GUI.Label(new Rect(x, y + 8, bw, 44), CurrentPanel == UiPanel.Weapons ? "WEAPON DEALER" : "ARMOR LOCKER", sBig);
        GUI.Label(new Rect(x + 20, y + 56, bw - 40, 28), "Your money: $" + NativeCore.Profile(NativeCore.Money), sLabel);
        float ry = y + 92;

        if (CurrentPanel == UiPanel.Weapons)
        {
            for (int i = 0; i < NativeCore.WeaponCount; i++)
            {
                var w = NativeCore.Weapons[i];
                bool owned = NativeCore.OwnsWeapon(i);
                string pel = w.pellets > 1 ? " x" + (int)w.pellets : "";
                GUI.Label(new Rect(x + 20, ry, 560, 28),
                    NativeCore.WeaponNames[i] + "   DMG " + (int)w.damage + pel + "   " + (1f / w.interval).ToString("0.0") + "/s   MAG " + (int)w.mag, sSmall);
                GUI.enabled = !owned && NativeCore.Profile(NativeCore.Money) >= (int)w.price;
                if (GUI.Button(new Rect(x + bw - 190, ry - 2, 170, 32), owned ? "OWNED" : "BUY  $" + (int)w.price, sBtn)) BuyWeapon(i);
                GUI.enabled = true;
                ry += 46;
            }
        }
        else
        {
            int cur = NativeCore.Profile(NativeCore.Vest);
            for (int lv = 1; lv < NativeCore.VestCount; lv++)
            {
                bool owned = cur >= lv;
                GUI.Label(new Rect(x + 20, ry, 560, 28), NativeCore.VestNames[lv] + "   Armor " + (int)NativeCore.VestArmor[lv], sSmall);
                GUI.enabled = !owned && NativeCore.Profile(NativeCore.Money) >= (int)NativeCore.VestPrice[lv];
                if (GUI.Button(new Rect(x + bw - 190, ry - 2, 170, 32), owned ? "OWNED" : "BUY  $" + (int)NativeCore.VestPrice[lv], sBtn)) BuyVest(lv);
                GUI.enabled = true;
                ry += 46;
            }
        }

        if (GUI.Button(new Rect(x + bw / 2 - 90, y + bh - 56, 180, 38), "Close (Esc)", sBtn)) CurrentPanel = UiPanel.None;
    }
}
