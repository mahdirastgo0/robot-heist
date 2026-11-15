using UnityEngine;

public class GunSlot
{
    public GameObject go;
    public Transform muzzle, rGrip, lGrip;
    public Vector3 aim;      // (مربوط به آدم ساده) جای اسلحه موقع نشانه‌گیری
    public Vector3 rLocal, lLocal;   // دستگیره‌ی راست/چپ در فضای اسلحه (با اندازه‌ی واقعی)
}

// انیمیشن آدم: راه رفتن، دویدن، حالت نشانه‌گیری. مدل رو GameManager.BuildHuman می‌سازه.
public class Humanoid : MonoBehaviour
{
    public Transform body, hipL, hipR, kneeL, kneeR, shoulderL, shoulderR, elbowL, elbowR, gunMount, gun, bag;
    public GameObject vest;
    public Renderer statusLight;
    public BonePoser poser;            // اگه مدل FBX داشته باشیم، استخوان‌ها با این حرکت می‌کنن
    public GunSlot[] slots;            // مدل‌های اسلحه (برای بازیکن ۵ تا، برای پلیس فقط تپانچه)
    int cur;
    public Vector3 gunLow = new Vector3(0.22f, 0.02f, 0.2f), gunAim = new Vector3(0.18f, 0.5f, 0.45f);
    public float speed;            // سرعت حرکت (m/s) - صاحب مدل هر فریم تنظیم می‌کنه
    public bool aiming;            // دست‌ها به سمت جلو و اسلحه بالا
    public System.Action onStep;   // هر بار پا به زمین میخوره

    float phase, ampS, aimT, lastSin;
    static readonly Color[] VestCol = { Color.black, new Color(0.35f, 0.4f, 0.5f), new Color(0.25f, 0.35f, 0.25f), new Color(0.1f, 0.1f, 0.12f) };

    // اسلحه‌ی شماره w رو نشون بده (مدل FBX/bytes یا مکعب ساده)
    public void ShowGun(int w)
    {
        if (slots != null && slots.Length > 0)
        {
            cur = Mathf.Clamp(w, 0, slots.Length - 1);
            for (int i = 0; i < slots.Length; i++) if (slots[i] != null) slots[i].go.SetActive(i == cur);
            if (gun != null) gun.gameObject.SetActive(false);
        }
        else if (gun != null) gun.gameObject.SetActive(true);
    }

    public Vector3 MuzzlePoint(float cubeLen)
    {
        if (slots != null && slots.Length > 0 && slots[cur] != null && slots[cur].muzzle != null) return slots[cur].muzzle.position;
        return gun.TransformPoint(0f, 0f, cubeLen * 0.5f + 0.03f);
    }

    public void SetVest(int level)
    {
        if (vest == null) return;
        vest.SetActive(level > 0);
        if (level > 0) vest.GetComponent<Renderer>().material.color = VestCol[Mathf.Clamp(level, 0, 3)];
    }

    public void SetBag(float fill)
    {
        if (bag != null) bag.localScale = Vector3.one * (0.8f + 0.7f * Mathf.Clamp01(fill));
    }

    float mphase;

    // ---- مدل FBX: دویدن/راه رفتن و گرفتن اسلحه تو دست راست (و دست چپ موقع نشانه‌گیری)
    void UpdateModel(float dt)
    {
        float runT = Mathf.Clamp01((speed - 5.5f) / 3.5f);                 // 0 = دویدن آروم، 1 = اسپرینت
        float freq = Mathf.Lerp(1.1f, 2.4f, Mathf.Clamp01(speed / 9f));     // چرخه در ثانیه
        float prev = Mathf.Sin(mphase);
        mphase += 2f * Mathf.PI * freq * dt * (speed > 0.15f ? 1f : 0f);
        float cur2 = Mathf.Sin(mphase);
        float amp = Mathf.Clamp01(speed / 3.5f);
        ampS = Mathf.MoveTowards(ampS, amp, dt * 5f);
        if (ampS > 0.25f && (cur2 > 0f) != (prev > 0f) && onStep != null) onStep();

        GunSlot slot = (slots != null && slots.Length > 0) ? slots[cur] : null;
        Vector3 sh = poser.ShoulderMid; float L = Mathf.Max(0.3f, poser.ArmLen);
        Vector3 aimPos = new Vector3(0f, sh.y - 0.08f, 0.66f * L);
        if (slot != null) aimPos.z = Mathf.Min(0.66f * L, 0.93f * L - Mathf.Abs(slot.lLocal.z));
        Vector3 carryPos = new Vector3(sh.x + 0.30f * poser.RightSign, sh.y - 0.55f, 0.30f);
        Vector3 gunPos = Vector3.Lerp(carryPos, aimPos, aimT);
        Quaternion gunRot = Quaternion.Slerp(Quaternion.Euler(35f, 0f, 0f), Quaternion.identity, aimT);
        gunMount.localPosition = gunPos;
        gunMount.localRotation = gunRot;

        Vector3 rl = slot != null ? slot.rLocal : Vector3.zero, ll = slot != null ? slot.lLocal : Vector3.zero;
        poser.Pose(mphase, ampS, runT, aimT, slot != null, gunPos, gunRot, rl, ll);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        aimT = Mathf.MoveTowards(aimT, aiming ? 1f : 0f, dt * 8f);
        if (poser != null) { UpdateModel(dt); return; }
        ampS = Mathf.MoveTowards(ampS, Mathf.Clamp01(speed / 6f), dt * 6f);
        phase += speed * dt * 1.2f;

        float sn = Mathf.Sin(phase);
        if (ampS > 0.25f && (sn > 0f) != (lastSin > 0f) && onStep != null) onStep();
        lastSin = sn;
        float swing = sn * ampS * 45f;

        // پاها (زاویه‌ی مثبت = به عقب، منفی = به جلو)
        hipL.localRotation = Quaternion.Euler(swing, 0f, 0f);
        hipR.localRotation = Quaternion.Euler(-swing, 0f, 0f);
        kneeL.localRotation = Quaternion.Euler(Mathf.Max(0f, -swing) * 1.1f, 0f, 0f);
        kneeR.localRotation = Quaternion.Euler(Mathf.Max(0f, swing) * 1.1f, 0f, 0f);

        // دست‌ها: تاب خوردن مخالف پا، یا نشانه‌گیری
        Quaternion idleL = Quaternion.Euler(-swing * 0.8f, 0f, 4f);
        Quaternion idleR = Quaternion.Euler(swing * 0.8f, 0f, -4f);
        shoulderL.localRotation = Quaternion.Slerp(idleL, Quaternion.Euler(-72f, 28f, 0f), aimT);
        shoulderR.localRotation = Quaternion.Slerp(idleR, Quaternion.Euler(-82f, -4f, 0f), aimT);
        float bend = -(8f + ampS * 30f);
        elbowL.localRotation = Quaternion.Slerp(Quaternion.Euler(bend, 0f, 0f), Quaternion.Euler(-12f, 0f, 0f), aimT);
        elbowR.localRotation = Quaternion.Slerp(Quaternion.Euler(bend, 0f, 0f), Quaternion.Euler(-8f, 0f, 0f), aimT);

        // بالاتنه: نوسان و کمی خم شدن به جلو موقع دویدن
        body.localPosition = new Vector3(0f, 0.92f + Mathf.Abs(sn) * 0.04f * ampS, 0f);
        body.localRotation = Quaternion.Euler(ampS * 5f, 0f, 0f);

        // اسلحه: پایین کنار کمر ←→ بالا جلوی سینه
        gunMount.localPosition = Vector3.Lerp(gunLow, gunAim, aimT);
        gunMount.localRotation = Quaternion.Euler(Mathf.Lerp(50f, 0f, aimT), 0f, 0f);
    }
}
