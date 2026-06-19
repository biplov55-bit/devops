
// HealthArmorSystem.cs
// FIXES:
//  • Zone damage now bypasses vest (zone damage should never be reduced by armor in BR games)
//  • Fall damage now handled (was silently ignored before)
//  • Melee damage bypasses both armor pieces (intentional design, now explicit)
//  • Vehicle damage hits vest only (body impact, not head)

using System;
using UnityEngine;

namespace FreeFire
{
    public enum DamageSource { Bullet, Headshot, Grenade, Zone, Melee, Fall, Vehicle }
    public enum ArmorSlot    { Helmet, Vest }

    [Serializable]
    public class ArmorPiece
    {
        public ArmorSlot slot;
        [Range(1, 3)] public int  level;
        public float maxDurability;
        public float curDurability;
        public float reduction;   // e.g. 0.25 = 25% damage absorbed

        public bool  IsActive      => curDurability > 0f;
        public float DurabilityPct => curDurability / maxDurability;

        public float Absorb(float incoming)
        {
            if (!IsActive) return incoming;
            float absorbed    = Mathf.Min(curDurability, incoming * reduction);
            curDurability     = Mathf.Max(0f, curDurability - absorbed);
            return incoming - absorbed;
        }

        public static ArmorPiece MakeHelmet(int lvl) => new()
        {
            slot = ArmorSlot.Helmet, level = lvl,
            maxDurability = 30f + lvl * 20f, curDurability = 30f + lvl * 20f,
            reduction     = 0.15f + lvl * 0.05f
        };

        public static ArmorPiece MakeVest(int lvl) => new()
        {
            slot = ArmorSlot.Vest, level = lvl,
            maxDurability = 50f + lvl * 30f, curDurability = 50f + lvl * 30f,
            reduction     = 0.20f + lvl * 0.05f
        };
    }

    public class HealthArmorSystem : MonoBehaviour
    {
        [Header("Health")]
        [SerializeField] private float baseHP = 100f;
        [SerializeField] private float maxHP  = 200f;  // expandable via items

        private float _hp;
        private float _currentMaxHP;
        private bool  _isKnockedDown;
        private bool  _isDead;
        private float _knockdownCountdown;
        private const float KnockdownDuration = 30f;

        private ArmorPiece _helmet;
        private ArmorPiece _vest;

        // ── Events ───────────────────────────────────────────────────────────
        public event Action<float, float> OnHealthChanged;  // (current, max)
        public event Action<ArmorPiece>   OnArmorEquipped;
        public event Action<float>        OnDamageTaken;    // processed damage
        public event Action               OnKnockdown;
        public event Action               OnRevived;
        public event Action               OnDeath;

        public float      HP            => _hp;
        public float      MaxHP         => _currentMaxHP;
        public float      HPPercent     => _hp / _currentMaxHP;
        public bool       IsKnockedDown => _isKnockedDown;
        public bool       IsDead        => _isDead;
        public ArmorPiece Helmet        => _helmet;
        public ArmorPiece Vest          => _vest;
        public float      KnockdownLeft => _knockdownCountdown;

        private void Awake()
        {
            _currentMaxHP = baseHP;
            _hp           = baseHP;
        }

        private void Update()
        {
            if (!_isKnockedDown || _isDead) return;
            _knockdownCountdown -= Time.deltaTime;
            if (_knockdownCountdown <= 0f) FinalizeDeath();
        }

        // ── Damage ───────────────────────────────────────────────────────────
        public void TakeDamage(float rawDmg, DamageSource src = DamageSource.Bullet)
        {
            if (_isDead || _isKnockedDown) return;

            float dmg = rawDmg;

            // FIX: Clear, correct armor absorption logic per damage source
            switch (src)
            {
                case DamageSource.Headshot:
                    // Helmet absorbs headshots only
                    if (_helmet != null) dmg = _helmet.Absorb(dmg);
                    break;

                case DamageSource.Bullet:
                case DamageSource.Vehicle:
                    // Vest absorbs body shots and vehicle impacts
                    if (_vest != null) dmg = _vest.Absorb(dmg);
                    break;

                case DamageSource.Grenade:
                    // Grenade hits both (blast can hit head or body)
                    if (_helmet != null) dmg = _helmet.Absorb(dmg * 0.4f) + dmg * 0.6f; // 40% to helmet
                    if (_vest   != null) dmg = _vest.Absorb(dmg);
                    break;

                case DamageSource.Zone:
                case DamageSource.Fall:
                case DamageSource.Melee:
                    // FIX: Zone, fall, and melee bypass ALL armor — raw damage only
                    break;
            }

            _hp = Mathf.Max(0f, _hp - dmg);
            OnHealthChanged?.Invoke(_hp, _currentMaxHP);
            OnDamageTaken?.Invoke(dmg);

            if (_hp <= 0f) BeginKnockdown();
        }

        // ── Healing ──────────────────────────────────────────────────────────
        public void Heal(float amount)
        {
            if (_isDead || _isKnockedDown) return;
            _hp = Mathf.Min(_hp + amount, _currentMaxHP);
            OnHealthChanged?.Invoke(_hp, _currentMaxHP);
        }

        public void ExpandMaxHP(float bonus)
        {
            _currentMaxHP = Mathf.Min(_currentMaxHP + bonus, maxHP);
            _hp           = Mathf.Min(_hp + bonus, _currentMaxHP);
            OnHealthChanged?.Invoke(_hp, _currentMaxHP);
        }

        // ── Armor ────────────────────────────────────────────────────────────
        public void EquipArmor(ArmorPiece piece)
        {
            if (piece.slot == ArmorSlot.Helmet) _helmet = piece;
            else                                _vest   = piece;
            OnArmorEquipped?.Invoke(piece);
        }

        // ── Knockdown / Death ─────────────────────────────────────────────────
        private void BeginKnockdown()
        {
            _isKnockedDown      = true;
            _knockdownCountdown = KnockdownDuration;
            _hp                 = 0f;
            OnKnockdown?.Invoke();
        }

        public void Revive(float reviveHP = 30f)
        {
            if (!_isKnockedDown || _isDead) return;
            _isKnockedDown = false;
            _hp            = reviveHP;
            OnHealthChanged?.Invoke(_hp, _currentMaxHP);
            OnRevived?.Invoke();
        }

        private void FinalizeDeath()
        {
            _isKnockedDown = false;
            _isDead        = true;
            OnDeath?.Invoke();
            GameManager.Instance?.RegisterElimination(CompareTag("LocalPlayer"), "Zone");
        }
    }
}
