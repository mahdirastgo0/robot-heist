// RobotHeistCore - هسته‌ی نیتیو بازی (C++17)
// مسیریابی BFS، جدول اسلحه/جلیقه، اقتصاد (فروش لوت، خرید)، ذخیره‌ی پروفایل بازیکن
#include <algorithm>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <queue>
#include <string>
#include <vector>

#ifdef _WIN32
#define RH_API extern "C" __declspec(dllexport)
#else
#define RH_API extern "C" __attribute__((visibility("default")))
#endif

namespace {

// ---------------- گرید مسیریابی ----------------
int g_cols = 0, g_rows = 0;
std::vector<char> g_wall;

bool walkable(int x, int y) {
    return x >= 0 && y >= 0 && x < g_cols && y < g_rows && !g_wall[y * g_cols + x];
}

// ---------------- پروفایل ----------------
enum Field { MONEY = 0, WEAPON_MASK, VEST, RUNS, EARNED, KILLS, BEST_RUN, ROBBERIES, FIELD_COUNT };
int g_profile[FIELD_COUNT] = {0, 1, 0, 0, 0, 0, 0, 0};  // شروع: فقط تپانچه
std::string g_savePath;

void saveProfile() {
    if (g_savePath.empty()) return;
    std::ofstream out(g_savePath, std::ios::trunc);
    for (int i = 0; i < FIELD_COUNT; i++) out << g_profile[i] << (i + 1 < FIELD_COUNT ? ' ' : '\n');
}

void loadProfile() {
    if (g_savePath.empty()) return;
    std::ifstream in(g_savePath);
    int tmp[FIELD_COUNT];
    for (int i = 0; i < FIELD_COUNT; i++)
        if (!(in >> tmp[i])) return;  // فایل ناقص → پیش‌فرض
    for (int i = 0; i < FIELD_COUNT; i++) g_profile[i] = std::max(0, tmp[i]);
    g_profile[WEAPON_MASK] |= 1;
}

// ---------------- جدول‌ها ----------------
struct Weapon { float damage, interval, mag, reload, spread, range, price, pellets; };
const Weapon kWeapons[5] = {
    //  dmg   interval mag   reload spread range price   pellets
    {28.f, 0.28f, 15.f, 1.0f, 1.0f, 75.f, 0.f, 1.f},       // 0 Pistol
    {14.f, 0.075f, 35.f, 1.3f, 3.0f, 60.f, 1500.f, 1.f},   // 1 SMG
    {11.f, 0.80f, 8.f, 1.8f, 6.0f, 40.f, 3000.f, 8.f},     // 2 Shotgun
    {24.f, 0.11f, 30.f, 1.7f, 1.5f, 100.f, 5500.f, 1.f},   // 3 Assault Rifle
    {28.f, 0.07f, 100.f, 3.0f, 2.5f, 110.f, 12000.f, 1.f}  // 4 LMG
};
const int kWeaponCount = 5;

struct Vest { float armor, price; };
const Vest kVests[4] = {{0.f, 0.f}, {60.f, 800.f}, {110.f, 2500.f}, {170.f, 6000.f}};
const int kVestCount = 4;

// آیتم‌های سرقت: ارزش، نوع مغازه (0 خواروبار، 1 الکترونیک، 2 جواهری، 3 بانک)، و آیتم خاص (کمیاب)
struct Item { int value; int shop; int special; };
const Item kItems[19] = {
    {220, 0, 0}, {180, 0, 0}, {420, 0, 0}, {1350, 0, 1},                  // 0-3   Cash Roll, Cigarettes, Whiskey, [Vintage Wine]
    {670, 1, 0}, {1200, 1, 0}, {900, 1, 0}, {1050, 1, 0}, {3300, 1, 1},    // 4-8   Phone, Laptop, Console, Camera, [Prototype Chip]
    {1200, 2, 0}, {1800, 2, 0}, {2400, 2, 0}, {1650, 2, 0}, {6750, 2, 1}, // 9-13  Necklace, Watch, Ring, Pearls, [Royal Ruby]
    {3000, 3, 0}, {4500, 3, 0}, {5250, 3, 0}, {6300, 3, 0}, {12000, 3, 1} // 14-18 Cash Bundle, Gold Bar, Bonds, Deposit Box, [Cartel Ledger]
};
const int kItemCount = 19;
const int kSpecialChance = 1200;  // از 10000 → ۱۲٪ شانس آیتم خاص

}  // namespace

RH_API int rh_init(const char* map, int cols, int rows, const char* save_path) {
    if (!map || cols <= 0 || rows <= 0 || (int)std::strlen(map) < cols * rows) return 0;
    g_cols = cols;
    g_rows = rows;
    g_wall.assign(cols * rows, 0);
    for (int i = 0; i < cols * rows; i++) g_wall[i] = (map[i] == '#');
    g_savePath = save_path ? save_path : "";
    loadProfile();
    return 1;
}

// جفت‌های x,y (بدون سلول شروع). برمی‌گردونه تعداد جفت‌ها؛ ۰ یعنی مسیری نیست
RH_API int rh_find_path(int fx, int fy, int tx, int ty, int* out_xy, int max_pairs) {
    if (!out_xy || max_pairs <= 0) return 0;
    if (!walkable(fx, fy) || !walkable(tx, ty) || (fx == tx && fy == ty)) return 0;

    const int N = g_cols * g_rows;
    std::vector<int> prev(N, -1);
    std::vector<char> seen(N, 0);
    std::queue<int> q;
    int start = fy * g_cols + fx, goal = ty * g_cols + tx;
    q.push(start);
    seen[start] = 1;
    static const int dx[4] = {0, 0, -1, 1}, dy[4] = {-1, 1, 0, 0};

    while (!q.empty()) {
        int c = q.front();
        q.pop();
        if (c == goal) break;
        int cx = c % g_cols, cy = c / g_cols;
        for (int d = 0; d < 4; d++) {
            int nx = cx + dx[d], ny = cy + dy[d];
            if (!walkable(nx, ny)) continue;
            int n = ny * g_cols + nx;
            if (seen[n]) continue;
            seen[n] = 1;
            prev[n] = c;
            q.push(n);
        }
    }
    if (!seen[goal]) return 0;

    std::vector<int> rev;
    for (int c = goal; c != start; c = prev[c]) rev.push_back(c);
    std::reverse(rev.begin(), rev.end());
    int n = std::min<int>((int)rev.size(), max_pairs);
    for (int i = 0; i < n; i++) {
        out_xy[i * 2] = rev[i] % g_cols;
        out_xy[i * 2 + 1] = rev[i] / g_cols;
    }
    return n;
}

// ---------------- پروفایل ----------------
RH_API int rh_profile_get(int field) {
    return (field >= 0 && field < FIELD_COUNT) ? g_profile[field] : 0;
}

RH_API void rh_profile_add(int field, int delta) {
    if (field < 0 || field >= FIELD_COUNT) return;
    g_profile[field] = std::max(0, g_profile[field] + delta);
    saveProfile();
}

RH_API void rh_reset_profile() {
    const int def[FIELD_COUNT] = {0, 1, 0, 0, 0, 0, 0, 0};
    std::memcpy(g_profile, def, sizeof(def));
    saveProfile();
}

// ---------------- جدول اسلحه و جلیقه ----------------
RH_API int rh_weapon_count() { return kWeaponCount; }

// stat: 0 damage, 1 interval, 2 mag, 3 reload, 4 spread, 5 range, 6 price, 7 pellets
RH_API float rh_weapon_stat(int idx, int stat) {
    if (idx < 0 || idx >= kWeaponCount) return 0.f;
    const Weapon& w = kWeapons[idx];
    const float v[8] = {w.damage, w.interval, w.mag, w.reload, w.spread, w.range, w.price, w.pellets};
    return (stat >= 0 && stat < 8) ? v[stat] : 0.f;
}

// stat: 0 armor, 1 price
RH_API float rh_vest_stat(int level, int stat) {
    if (level < 0 || level >= kVestCount) return 0.f;
    return stat == 0 ? kVests[level].armor : kVests[level].price;
}

// 1 = خرید موفق، 0 = پول کافی نیست، -1 = از قبل داری / نامعتبر
RH_API int rh_buy_weapon(int idx) {
    if (idx < 0 || idx >= kWeaponCount) return -1;
    if (g_profile[WEAPON_MASK] & (1 << idx)) return -1;
    int price = (int)kWeapons[idx].price;
    if (g_profile[MONEY] < price) return 0;
    g_profile[MONEY] -= price;
    g_profile[WEAPON_MASK] |= (1 << idx);
    saveProfile();
    return 1;
}

RH_API int rh_buy_vest(int level) {
    if (level < 1 || level >= kVestCount) return -1;
    if (level <= g_profile[VEST]) return -1;
    int price = (int)kVests[level].price;
    if (g_profile[MONEY] < price) return 0;
    g_profile[MONEY] -= price;
    g_profile[VEST] = level;
    saveProfile();
    return 1;
}

// ---------------- اقتصاد ----------------
RH_API int rh_item_count() { return kItemCount; }
RH_API int rh_item_value(int id) { return (id >= 0 && id < kItemCount) ? kItems[id].value : 0; }
RH_API int rh_item_special(int id) { return (id >= 0 && id < kItemCount) ? kItems[id].special : 0; }

// انتخاب آیتم از مغازه‌ی shop_type؛ rnd: 0..9999. برمی‌گردونه id آیتم (-1 = نامعتبر)
RH_API int rh_loot_pick(int shop_type, int rnd) {
    if (shop_type < 0 || shop_type > 3) return -1;
    rnd = std::max(0, std::min(9999, rnd));
    std::vector<int> regular;
    int special = -1;
    for (int i = 0; i < kItemCount; i++) {
        if (kItems[i].shop != shop_type) continue;
        if (kItems[i].special) special = i; else regular.push_back(i);
    }
    if (rnd < kSpecialChance && special >= 0) return special;
    if (regular.empty()) return special;
    int idx = (int)((long long)(rnd - kSpecialChance) * (long long)regular.size() / (10000 - kSpecialChance));
    return regular[std::min<int>(idx, (int)regular.size() - 1)];
}

// قیمت فروش در بلک‌مارکت؛ mult_x100 = ضریب بازار × ۱۰۰ (مثلاً ۱۲۵)
RH_API int rh_sell_price(int value, int mult_x100) {
    return (int)((long long)std::max(0, value) * std::max(0, mult_x100) / 100);
}

// وقتی بازیکن سالم به بیس برسه، پول این دور تو حسابش ذخیره میشه
RH_API void rh_bank_run(int cash) {
    cash = std::max(0, cash);
    g_profile[MONEY] += cash;
    g_profile[EARNED] += cash;
    g_profile[RUNS] += 1;
    g_profile[BEST_RUN] = std::max(g_profile[BEST_RUN], cash);
    saveProfile();
}
