#include <cassert>
#include <cstdio>
extern "C" {
int rh_init(const char*, int, int, const char*);
int rh_find_path(int, int, int, int, int*, int);
int rh_profile_get(int);
void rh_profile_add(int, int);
void rh_reset_profile();
int rh_weapon_count();
float rh_weapon_stat(int, int);
float rh_vest_stat(int, int);
int rh_buy_weapon(int);
int rh_buy_vest(int);
int rh_item_count();
int rh_item_value(int);
int rh_item_special(int);
int rh_loot_pick(int, int);
int rh_sell_price(int, int);
void rh_bank_run(int);
}
int main() {
    const char* map = "#####"
                      "#..##"
                      "#.#.#"
                      "#...#"
                      "#####";
    const char* save = "/tmp/rh_profile_test.txt";
    assert(rh_init(map, 5, 5, save) == 1);
    rh_reset_profile();

    int out[64];
    assert(rh_find_path(1, 1, 3, 3, out, 32) == 4);
    assert(rh_find_path(1, 1, 0, 0, out, 32) == 0);

    assert(rh_weapon_count() == 5);
    assert(rh_weapon_stat(0, 6) == 0.f && rh_weapon_stat(1, 6) == 1500.f);
    assert(rh_vest_stat(2, 0) == 110.f);

    assert(rh_profile_get(0) == 0);
    assert(rh_buy_weapon(1) == 0);          // پول نداره
    assert(rh_buy_weapon(0) == -1);         // تپانچه از قبل داره
    rh_bank_run(3000);
    assert(rh_profile_get(0) == 3000 && rh_profile_get(3) == 1 && rh_profile_get(6) == 3000);
    assert(rh_buy_weapon(1) == 1);
    assert(rh_profile_get(0) == 1500);
    assert((rh_profile_get(1) & 2) != 0);
    assert(rh_buy_vest(3) == 0);            // پولش نمیرسه
    rh_bank_run(2000);
    assert(rh_buy_vest(1) == 1 && rh_profile_get(2) == 1);
    assert(rh_buy_vest(1) == -1);

    assert(rh_item_count() == 19);
    assert(rh_loot_pick(2, 0) == 13 && rh_item_special(13) == 1);      // شانس خاص
    assert(rh_loot_pick(2, 9999) == 12 && rh_item_special(12) == 0);   // آیتم معمولی
    assert(rh_loot_pick(3, 1200) == 14);
    assert(rh_loot_pick(9, 0) == -1);
    for (int shop = 0; shop < 4; shop++)
        for (int r = 0; r < 10000; r += 7) {
            int id = rh_loot_pick(shop, r);
            assert(id >= 0 && id < 19 && rh_item_value(id) > 0);
        }
    assert(rh_item_value(18) == 12000);
    assert(rh_sell_price(1000, 125) == 1250);

    // ذخیره/بارگذاری
    assert(rh_init(map, 5, 5, save) == 1);
    assert(rh_profile_get(2) == 1 && (rh_profile_get(1) & 2) != 0);
    puts("ALL OK");
}
