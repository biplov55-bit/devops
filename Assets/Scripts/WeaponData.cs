
// WeaponData.cs — No bugs. Unchanged.
using UnityEngine;
namespace FreeFire
{
    public enum WeaponType { AssaultRifle, SMG, SniperRifle, Shotgun, Pistol, Melee }
    public enum FireMode   { Auto, SemiAuto, Burst }

    [CreateAssetMenu(fileName = "WPN_New", menuName = "FreeFire/Weapon Data", order = 0)]
    public class WeaponData : ScriptableObject
    {
        [Header("Identity")]
        public string     weaponName   = "New Weapon";
        public WeaponType type;
        public Sprite     icon;
        public GameObject worldPrefab;
        public GameObject bulletPrefab; // null = hitscan

        [Header("Combat")]
        public FireMode fireMode     = FireMode.Auto;
        public float    damage       = 28f;
        public float    headshotMult = 2.0f;
        public float    range        = 120f;
        public float    rateOfFire   = 550f; // rounds/min

        [Header("Accuracy")]
        [Range(0f, 8f)] public float baseSpread  = 1.2f;
        [Range(0f, 8f)] public float aimSpread   = 0.15f;
        [Range(0f, 8f)] public float movingAdd   = 2.0f;
        public AnimationCurve spreadGrowth = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Ammo")]
        public int   magCap         = 30;
        public int   maxReserve     = 120;
        public float reloadTime     = 2.1f;
        public bool  tacticalReload = true;

        [Header("Recoil Pattern")]
        public Vector2[]      recoilPattern;
        public float          recoilRecovery     = 9f;
        public AnimationCurve recoilScaleOverMag = AnimationCurve.Linear(0, 1, 1, 1.4f);

        [Header("Projectile (non-hitscan)")]
        public float projSpeed   = 60f;
        public float projGravity = 0f;

        [Header("ADS")]
        public float  adsTime   = 0.22f;
        public Sprite adsSprite;

        [Header("SFX Keys")]
        public string sfxShoot  = "sfx_shoot_ar";
        public string sfxEmpty  = "sfx_empty";
        public string sfxReload = "sfx_reload_ar";

        [Header("VFX Pool Keys")]
        public string vfxMuzzle        = "VFX_Muzzle";
        public string vfxImpactDefault = "VFX_Impact";
        public string vfxImpactMetal   = "VFX_Impact_Metal";

        public float FireInterval => 60f / rateOfFire;
        public bool  IsHitscan   => bulletPrefab == null;
    }
}