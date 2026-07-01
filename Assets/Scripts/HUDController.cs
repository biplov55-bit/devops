using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FreeFire
{
    public class HUDController : MonoBehaviour
    {
        [Header("Health & Armor")]
        [SerializeField] private Slider          healthBar;
        [SerializeField] private Slider          helmetBar;
        [SerializeField] private Slider          vestBar;
        [SerializeField] private TextMeshProUGUI hpText;

        [Header("Ammo")]
        [SerializeField] private TextMeshProUGUI magText;
        [SerializeField] private TextMeshProUGUI reserveText;

        [Header("Zone")]
        [SerializeField] private TextMeshProUGUI zoneText;
        [SerializeField] private Image           zoneBorder;
        [SerializeField] private Gradient        zoneDangerGradient;

        [Header("Kill / Score")]
        [SerializeField] private TextMeshProUGUI killText;
        [SerializeField] private TextMeshProUGUI aliveText;

        [Header("Crosshair")]
        [SerializeField] private RectTransform[] crosshairLines;
        [SerializeField] private float           crosshairSpread = 30f;
        [SerializeField] private float           crosshairSmooth = 18f;

        private HealthArmorSystem _health;
        private WeaponController  _weapons;
        private SafeZoneManager   _zone;
        private GameManager       _game;

        private float _targetSpread;
        private float _currentSpread;

        private void Start()
        {
            // FIX: FindAnyObjectByType — no instance-ordering dependency (Unity 6)
            _health  = FindAnyObjectByType<HealthArmorSystem>();
            _weapons = FindAnyObjectByType<WeaponController>();
            _zone    = FindAnyObjectByType<SafeZoneManager>();
            _game    = GameManager.Instance;

            if (_health  != null) { _health.OnHealthChanged  += OnHealthChanged;
                                    _health.OnArmorEquipped   += OnArmorEquipped; }
            if (_weapons != null && _weapons.ActiveWeapon != null)
                                    _weapons.ActiveWeapon.OnAmmoChanged += OnAmmoChanged;
            if (_zone    != null) { _zone.OnCountdownTick     += OnZoneTick;
                                    _zone.OnZoneStopped       += OnZoneStopped; }
            if (_game    != null) { _game.OnKillCountChanged  += OnKillsChanged;
                                    _game.OnPlayersAliveChanged += OnAliveChanged; }

            RefreshAll();
        }

        private void OnDestroy()
        {
            if (_health  != null) { _health.OnHealthChanged  -= OnHealthChanged;
                                    _health.OnArmorEquipped   -= OnArmorEquipped; }
            if (_weapons != null && _weapons.ActiveWeapon != null)
                                    _weapons.ActiveWeapon.OnAmmoChanged -= OnAmmoChanged;
            if (_zone    != null) { _zone.OnCountdownTick     -= OnZoneTick;
                                    _zone.OnZoneStopped       -= OnZoneStopped; }
            if (_game    != null) { _game.OnKillCountChanged  -= OnKillsChanged;
                                    _game.OnPlayersAliveChanged -= OnAliveChanged; }
        }

        private void Update() => UpdateCrosshair();

        private void RefreshAll()
        {
            if (_health  != null) OnHealthChanged(_health.HP, _health.MaxHP);
            if (_weapons?.ActiveWeapon != null)
                OnAmmoChanged(_weapons.ActiveWeapon.Mag, _weapons.ActiveWeapon.Reserve);
        }

        private void OnHealthChanged(float hp, float max)
        {
            if (healthBar != null) healthBar.value = hp / max;
            if (hpText    != null) hpText.text = $"{Mathf.CeilToInt(hp)}/{Mathf.CeilToInt(max)}";
        }

        private void OnArmorEquipped(ArmorPiece p)
        {
            if (p.slot == ArmorSlot.Helmet && helmetBar != null) helmetBar.value = p.DurabilityPct;
            else if (p.slot == ArmorSlot.Vest && vestBar != null) vestBar.value = p.DurabilityPct;
        }

        private void OnAmmoChanged(int mag, int reserve)
        {
            if (magText     != null) magText.text     = mag.ToString();
            if (reserveText != null) reserveText.text = reserve.ToString();
        }

        private void OnZoneTick(float seconds)
        {
            if (zoneText != null) zoneText.text = $"ZONE: {Mathf.CeilToInt(seconds)}s";
        }

        private void OnZoneStopped()
        {
            if (zoneText != null) zoneText.text = "";
        }

        private void OnKillsChanged(int k)
        {
            if (killText != null) killText.text = $"Kills: {k}";
        }

        private void OnAliveChanged(int a)
        {
            if (aliveText != null) aliveText.text = $"Alive: {a}";
        }

        public void SetSpread(float spread) => _targetSpread = spread * crosshairSpread;

        private void UpdateCrosshair()
        {
            _currentSpread = Mathf.Lerp(_currentSpread, _targetSpread, Time.deltaTime * crosshairSmooth);
            if (crosshairLines == null) return;
            float half = _currentSpread * 0.5f;
            var offsets = new Vector2[]
            {
                new( 0,  half),
                new( 0, -half),
                new(-half,  0),
                new( half,  0)
            };
            for (int i = 0; i < crosshairLines.Length && i < offsets.Length; i++)
                crosshairLines[i].anchoredPosition = offsets[i];
        }
    }
}