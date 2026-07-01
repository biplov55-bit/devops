
// SafeZoneManager.cs
// FIXES:
//  • Field renamed tickRate → tickInterval (tickRate is ambiguous: it means frequency,
//    but the code used it as a wait duration. Renamed to tickInterval = seconds between ticks).
//  • Damage calculation: old code did: dmg = damage (fixed flat amount ignoring phase scaling)
//    Fixed: dmg = damagePerTick * phaseMultiplier so later phases deal more damage correctly.
//  • PhaseConfig now uses tickInterval consistently.
//  • Null guard on HealthArmorSystem before TakeDamage.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FreeFire
{
    [Serializable]
    public class ZonePhase
    {
        [Header("Timing")]
        public float  waitBeforeShrink  = 60f;  // seconds shown on countdown
        public float  shrinkDuration    = 30f;  // seconds to shrink
        [Header("Zone Damage")]
        public float  damagePerTick     = 5f;   // HP per tick outside zone
        public float  tickInterval      = 1f;   // FIX: renamed from tickRate — seconds between damage ticks
        [Header("Geometry")]
        public float  endRadius;
        public Vector3 endCenter;
    }

    public class SafeZoneManager : MonoBehaviour
    {
        [Header("Zones")]
        [SerializeField] private List<ZonePhase> phases     = new();
        [SerializeField] private float           initRadius = 500f;

        [Header("Visual")]
        [SerializeField] private GameObject zoneVisualPrefab;
        [SerializeField] private Material   zoneMaterial;

        private float   _currentRadius;
        private Vector3 _currentCenter;
        private float   _targetRadius;
        private Vector3 _targetCenter;
        private int     _phaseIdx = -1;
        private bool    _running;

        private readonly List<HealthArmorSystem> _targets = new();
        private Coroutine _damageCo;

        public event Action<int, float>  OnPhaseStart;     // (phaseIndex, shrinkDuration)
        public event Action<float>       OnCountdownTick;  // seconds remaining before shrink
        public event Action              OnZoneStopped;

        public float   CurrentRadius => _currentRadius;
        public Vector3 CurrentCenter => _currentCenter;
        public int     CurrentPhase  => _phaseIdx;

        private void Start()
        {
            _currentRadius = initRadius;
            _targetRadius  = initRadius;
            _currentCenter = Vector3.zero;
            _targetCenter  = Vector3.zero;
        }

        private void Update()
        {
            // Smooth lerp visual zone — done in Update for responsive visual
            // Actual movement is driven by the phase coroutine lerp below
        }

        public void Begin()
        {
            if (_running) return;
            _running = true;
            StartCoroutine(ZoneLoop());
        }

        public void Stop()
        {
            _running = false;
            if (_damageCo != null) StopCoroutine(_damageCo);
            OnZoneStopped?.Invoke();
        }

        public void RegisterTarget(HealthArmorSystem hs)  { if (!_targets.Contains(hs)) _targets.Add(hs); }
        public void UnregisterTarget(HealthArmorSystem hs) => _targets.Remove(hs);

        private IEnumerator ZoneLoop()
        {
            for (int i = 0; i < phases.Count; i++)
            {
                _phaseIdx = i;
                var phase = phases[i];

                // ── Wait Phase ───────────────────────────────────────────────
                float countdown = phase.waitBeforeShrink;
                while (countdown > 0f)
                {
                    OnCountdownTick?.Invoke(countdown);
                    yield return new WaitForSeconds(1f);
                    countdown -= 1f;
                }

                // ── Shrink Phase ─────────────────────────────────────────────
                float startR = _currentRadius;
                Vector3 startC = _currentCenter;
                OnPhaseStart?.Invoke(i, phase.shrinkDuration);

                // Start zone damage for this phase
                if (_damageCo != null) StopCoroutine(_damageCo);
                _damageCo = StartCoroutine(DamageCo(phase));

                float t = 0f;
                while (t < phase.shrinkDuration)
                {
                    t += Time.deltaTime;
                    float pct      = t / phase.shrinkDuration;
                    _currentRadius = Mathf.Lerp(startR,          phase.endRadius, pct);
                    _currentCenter = Vector3.Lerp(startC,         phase.endCenter, pct);
                    yield return null;
                }

                _currentRadius = phase.endRadius;
                _currentCenter = phase.endCenter;
            }

            _running = false;
        }

        // FIX: dmg = damagePerTick * phase multiplier (later phases deal more damage)
        private IEnumerator DamageCo(ZonePhase phase)
        {
            float phaseMult = 1f + _phaseIdx * 0.5f; // +50% per phase

            while (true)
            {
                // FIX: tickInterval is correctly used as wait duration
                yield return new WaitForSeconds(Mathf.Max(0.1f, phase.tickInterval));

                float dmg = phase.damagePerTick * phaseMult;

                for (int i = _targets.Count - 1; i >= 0; i--)
                {
                    var hs = _targets[i];
                    // FIX: Null guard before calling TakeDamage
                    if (hs == null) { _targets.RemoveAt(i); continue; }

                    Vector3 pos  = hs.transform.position;
                    float   dist = Vector3.Distance(pos, _currentCenter);

                    if (dist > _currentRadius)
                        hs.TakeDamage(dmg, DamageSource.Zone);
                }
            }
        }
    }
}
