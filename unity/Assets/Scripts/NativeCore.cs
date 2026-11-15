using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

// پل بین C# و هسته‌ی C++ (RobotHeistCore.dll داخل Assets/Plugins/x86_64)
public static class NativeCore
{
    const string Lib = "RobotHeistCore";
    const CallingConvention Conv = CallingConvention.Cdecl;

    // فیلدهای پروفایل
    public const int Money = 0, WeaponMask = 1, Vest = 2, Runs = 3, Earned = 4, Kills = 5, BestRun = 6, Robberies = 7;
    public const int WeaponCount = 5, VestCount = 4;

    public static readonly string[] WeaponNames = { "Pistol", "SMG", "Shotgun", "Assault Rifle", "LMG" };
    public static readonly string[] VestNames = { "No Vest", "Light Vest", "Medium Vest", "Heavy Vest" };

    // آیتم‌های سرقت (ترتیب دقیقاً مثل جدول C++). آیتم‌های خاص با [RARE] مشخص میشن
    public const int ItemCount = 19;
    public static readonly string[] ItemNames =
    {
        "Cash Roll", "Cigarette Carton", "Premium Whiskey", "Vintage Wine",
        "Smartphone", "Laptop", "Game Console", "Camera", "Prototype Chip",
        "Silver Necklace", "Gold Watch", "Diamond Ring", "Pearl Set", "Royal Ruby",
        "Cash Bundle", "Gold Bar", "Bearer Bonds", "Safe Deposit Box", "Cartel Ledger"
    };
    public static int[] ItemValue = new int[ItemCount];
    public static bool[] ItemSpecial = new bool[ItemCount];

    public struct WeaponStats
    {
        public float damage, interval, mag, reload, spread, range, price, pellets;
    }

    [DllImport(Lib, CallingConvention = Conv)]
    static extern int rh_init([MarshalAs(UnmanagedType.LPStr)] string map, int cols, int rows,
                              [MarshalAs(UnmanagedType.LPStr)] string savePath);
    [DllImport(Lib, CallingConvention = Conv)] static extern int rh_find_path(int fx, int fy, int tx, int ty, int[] outXY, int maxPairs);
    [DllImport(Lib, CallingConvention = Conv)] static extern int rh_profile_get(int field);
    [DllImport(Lib, CallingConvention = Conv)] static extern void rh_profile_add(int field, int delta);
    [DllImport(Lib, CallingConvention = Conv)] static extern float rh_weapon_stat(int idx, int stat);
    [DllImport(Lib, CallingConvention = Conv)] static extern float rh_vest_stat(int level, int stat);
    [DllImport(Lib, CallingConvention = Conv)] static extern int rh_buy_weapon(int idx);
    [DllImport(Lib, CallingConvention = Conv)] static extern int rh_buy_vest(int level);
    [DllImport(Lib, CallingConvention = Conv)] static extern int rh_loot_pick(int shopType, int rnd);
    [DllImport(Lib, CallingConvention = Conv)] static extern int rh_item_value(int id);
    [DllImport(Lib, CallingConvention = Conv)] static extern int rh_item_special(int id);
    [DllImport(Lib, CallingConvention = Conv)] static extern int rh_sell_price(int value, int multX100);
    [DllImport(Lib, CallingConvention = Conv)] static extern void rh_bank_run(int cash);

    public static bool Available { get; private set; }
    public static string Error { get; private set; } = "";

    public static WeaponStats[] Weapons = new WeaponStats[WeaponCount];
    public static float[] VestArmor = new float[VestCount];
    public static float[] VestPrice = new float[VestCount];

    static readonly int[] pathBuf = new int[2048];

    public static bool Init(string map, int cols, int rows, string savePath)
    {
        try
        {
            Available = rh_init(map, cols, rows, savePath) == 1;
            if (Available)
            {
                for (int i = 0; i < WeaponCount; i++)
                    Weapons[i] = new WeaponStats
                    {
                        damage = rh_weapon_stat(i, 0), interval = rh_weapon_stat(i, 1), mag = rh_weapon_stat(i, 2),
                        reload = rh_weapon_stat(i, 3), spread = rh_weapon_stat(i, 4), range = rh_weapon_stat(i, 5),
                        price = rh_weapon_stat(i, 6), pellets = rh_weapon_stat(i, 7)
                    };
                for (int i = 0; i < ItemCount; i++) { ItemValue[i] = rh_item_value(i); ItemSpecial[i] = rh_item_special(i) == 1; }
                for (int i = 0; i < VestCount; i++) { VestArmor[i] = rh_vest_stat(i, 0); VestPrice[i] = rh_vest_stat(i, 1); }
            }
        }
        catch (Exception e)   // DllNotFoundException / EntryPointNotFoundException
        {
            Available = false;
            Error = e.GetType().Name;
            Debug.LogError("[NativeCore] C++ library not loaded: " + e.Message);
        }
        return Available;
    }

    public static List<Vector2Int> FindPath(Vector2Int from, Vector2Int to)
    {
        var list = new List<Vector2Int>();
        int n = rh_find_path(from.x, from.y, to.x, to.y, pathBuf, pathBuf.Length / 2);
        for (int i = 0; i < n; i++) list.Add(new Vector2Int(pathBuf[i * 2], pathBuf[i * 2 + 1]));
        return list;
    }

    public static int Profile(int field) => rh_profile_get(field);
    public static void ProfileAdd(int field, int delta) => rh_profile_add(field, delta);
    public static bool OwnsWeapon(int i) => (rh_profile_get(WeaponMask) & (1 << i)) != 0;
    public static int BuyWeapon(int i) => rh_buy_weapon(i);
    public static int BuyVest(int level) => rh_buy_vest(level);
    public static int LootPick(int shopType, int rnd) => rh_loot_pick(shopType, rnd);
    public static int SellPrice(int value, int multX100) => rh_sell_price(value, multX100);
    public static void BankRun(int cash) => rh_bank_run(cash);
}
