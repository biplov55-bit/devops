// ObjectPoolManager.cs
// FIX: Race condition in Spawn() when pool is exhausted — Allocate() enqueues,
//      then we safely dequeue once. Old code had a double-dequeue risk.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FreeFire


{
    public class PooledObject : MonoBehaviour
    {
        public string PoolKey { get; internal set; }
        internal ObjectPoolManager OwnerPool;
        public void ReturnToPool() => OwnerPool?.Return(this);
        protected virtual void OnSpawnFromPool() { }
        protected virtual void OnReturnToPool()  { }
        internal void TriggerSpawn()  => OnSpawnFromPool();
        internal void TriggerReturn() => OnReturnToPool();
    }

    [Serializable]
    public class PoolConfig
    {
        public string     key;
        public GameObject prefab;
        [Min(1)] public int  initialSize = 10;
        [Min(1)] public int  maxSize     = 100;
        public bool expandable = true;
    }

    public class ObjectPoolManager : MonoBehaviour
    {
        public static ObjectPoolManager Instance { get; private set; }

        [Header("Pool Definitions")]
        [SerializeField] private List<PoolConfig> configs = new();

        private readonly Dictionary<string, Queue<PooledObject>> _pools   = new();
        private readonly Dictionary<string, PoolConfig>          _configs = new();
        private readonly Dictionary<string, Transform>           _folders = new();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            foreach (var cfg in configs) RegisterPool(cfg);
        }

        public void RegisterPool(PoolConfig cfg)
        {
            if (_pools.ContainsKey(cfg.key)) return;
            if (cfg.prefab == null) { Debug.LogError($"[Pool] Null prefab for key '{cfg.key}'"); return; }

            _configs[cfg.key] = cfg;
            _pools[cfg.key]   = new Queue<PooledObject>(cfg.initialSize);

            var folder = new GameObject($"[Pool] {cfg.key}");
            folder.transform.SetParent(transform);
            _folders[cfg.key] = folder.transform;

            Prewarm(cfg.key, cfg.initialSize);
        }

        public void Prewarm(string key, int count)
        {
            for (int i = 0; i < count; i++) Allocate(key);
        }

        public GameObject Spawn(string key, Vector3 pos, Quaternion rot)
        {
            if (!_pools.TryGetValue(key, out var queue))
            {
                Debug.LogError($"[Pool] Key '{key}' not registered."); return null;
            }

            // FIX: Consolidated dequeue — check count, expand if needed, then dequeue once.
            if (queue.Count == 0)
            {
                if (!_configs[key].expandable)
                {
                    Debug.LogWarning($"[Pool] '{key}' exhausted."); return null;
                }
                Allocate(key); // enqueues one new object
            }

            var po = queue.Dequeue(); // safe single dequeue
            po.transform.SetPositionAndRotation(pos, rot);
            po.gameObject.SetActive(true);
            po.TriggerSpawn();
            return po.gameObject;
        }

        public T Spawn<T>(string key, Vector3 pos, Quaternion rot) where T : Component
            => Spawn(key, pos, rot)?.GetComponent<T>();

        internal void Return(PooledObject po)
        {
            if (po == null) return;
            po.TriggerReturn();
            po.gameObject.SetActive(false);

            string key = po.PoolKey;
            if (!_pools.TryGetValue(key, out var queue) || queue.Count >= _configs[key].maxSize)
            {
                Destroy(po.gameObject); return;
            }
            po.transform.SetParent(_folders[key]);
            queue.Enqueue(po);
        }

        private PooledObject Allocate(string key)
        {
            var cfg = _configs[key];
            var go  = Instantiate(cfg.prefab, _folders[key]);
            var po  = go.GetComponent<PooledObject>() ?? go.AddComponent<PooledObject>();
            po.PoolKey   = key;
            po.OwnerPool = this;
            go.SetActive(false);
            _pools[key].Enqueue(po);
            return po;
        }
    }
}
