
// AudioManager.cs
// FIX: Coroutine stacking — if PlayMusic() is called while a fade is running,
//      the old coroutine is stopped first, preventing volume conflicts / memory leak.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FreeFire
{
    [System.Serializable]
    public class SoundEntry
    {
        public string     key;
        public AudioClip[] clips;
        [Range(0f, 1f)]   public float volume        = 1f;
        [Range(0f, 0.4f)] public float pitchVariance = 0.1f;
        public bool  spatial     = true;
        public float maxDistance = 150f;
    }

    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Sound Library")]
        [SerializeField] private List<SoundEntry> sounds    = new();
        [SerializeField] private int              poolSize  = 24;
        [SerializeField] private AudioSource      musicSource;

        private readonly Dictionary<string, SoundEntry> _map    = new();
        private readonly Queue<AudioSource>              _free   = new();
        private readonly List<AudioSource>               _active = new();

        // FIX: Track the running fade coroutine so it can be stopped before starting a new one.
        private Coroutine _fadeCo;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            foreach (var s in sounds) _map[s.key] = s;

            for (int i = 0; i < poolSize; i++)
            {
                var go  = new GameObject($"SFX_{i}");
                go.transform.SetParent(transform);
                var src = go.AddComponent<AudioSource>();
                go.SetActive(false);
                _free.Enqueue(src);
            }
        }

        private void Update()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].isPlaying) continue;
                _active[i].gameObject.SetActive(false);
                _free.Enqueue(_active[i]);
                _active.RemoveAt(i);
            }
        }

        public void PlayPositional(string key, Vector3 pos, float volumeMult = 1f)
        {
            if (!_map.TryGetValue(key, out var e) || e.clips == null || e.clips.Length == 0) return;
            var src = Rent(); if (src == null) return;

            src.transform.position = pos;
            src.clip         = e.clips[Random.Range(0, e.clips.Length)];
            src.volume       = e.volume * volumeMult;
            src.pitch        = 1f + Random.Range(-e.pitchVariance, e.pitchVariance);
            src.spatialBlend = 1f;
            src.rolloffMode  = AudioRolloffMode.Linear;
            src.maxDistance  = e.maxDistance;
            src.Play();
        }

        public void PlayUI(string key, float volumeMult = 1f)
        {
            if (!_map.TryGetValue(key, out var e) || e.clips == null || e.clips.Length == 0) return;
            var src = Rent(); if (src == null) return;

            src.transform.position = Vector3.zero;
            src.clip         = e.clips[Random.Range(0, e.clips.Length)];
            src.volume       = e.volume * volumeMult;
            src.pitch        = 1f + Random.Range(-e.pitchVariance, e.pitchVariance);
            src.spatialBlend = 0f;
            src.Play();
        }

        public void PlayMusic(AudioClip clip, float fadeIn = 1f)
        {
            if (musicSource == null) return;

            // FIX: Stop any in-progress fade before starting a new one.
            if (_fadeCo != null) StopCoroutine(_fadeCo);

            musicSource.Stop();
            musicSource.clip   = clip;
            musicSource.volume = 0f;
            musicSource.Play();
            _fadeCo = StartCoroutine(FadeInMusic(fadeIn));
        }

        public void StopMusic(float fadeOut = 1f)
        {
            if (musicSource == null) return;
            if (_fadeCo != null) StopCoroutine(_fadeCo);
            _fadeCo = StartCoroutine(FadeOutMusic(fadeOut));
        }

        private IEnumerator FadeInMusic(float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                musicSource.volume = Mathf.Clamp01(t / duration);
                yield return null;
            }
            musicSource.volume = 1f;
            _fadeCo = null;
        }

        private IEnumerator FadeOutMusic(float duration)
        {
            float start = musicSource.volume;
            float t     = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                musicSource.volume = Mathf.Lerp(start, 0f, t / duration);
                yield return null;
            }
            musicSource.Stop();
            _fadeCo = null;
        }

        private AudioSource Rent()
        {
            if (_free.Count == 0) return null;
            var src = _free.Dequeue();
            src.gameObject.SetActive(true);
            _active.Add(src);
            return src;
        }
    }
}
