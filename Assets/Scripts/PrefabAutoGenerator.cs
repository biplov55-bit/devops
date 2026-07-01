#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using System.IO;

namespace FreeFire.Editor
{
    public class PrefabAutoGenerator : EditorWindow
    {
        // ── Paths ────────────────────────────────────────────────────────
        const string ROOT    = "Assets/Prefabs/Pool";
        const string BULLETS = ROOT + "/Bullets";
        const string VFX     = ROOT + "/VFX";
        const string ENEMIES = ROOT + "/Enemies";

        // ── Menu Entry ───────────────────────────────────────────────────
        [MenuItem("FreeFire/Generate All Pool Prefabs")]
        public static void ShowWindow()
            => GetWindow<PrefabAutoGenerator>("Pool Prefab Generator");

        // ── GUI ──────────────────────────────────────────────────────────
        void OnGUI()
        {
            GUILayout.Space(10);
            GUILayout.Label("FreeFire — Pool Prefab Generator", EditorStyles.boldLabel);
            GUILayout.Space(5);
            EditorGUILayout.HelpBox(
                "Creates all prefabs needed by ObjectPoolManager.\n" +
                "Safe to re-run — skips prefabs that already exist.",
                MessageType.Info);
            GUILayout.Space(10);

            if (GUILayout.Button("⚡  Generate ALL Prefabs", GUILayout.Height(45)))
                GenerateAll();

            GUILayout.Space(5);

            if (GUILayout.Button("Generate Bullets Only", GUILayout.Height(30))) GenerateBullets();
            if (GUILayout.Button("Generate VFX Only",     GUILayout.Height(30))) GenerateVFX();
            if (GUILayout.Button("Generate Enemy Only",   GUILayout.Height(30))) GenerateEnemy();
        }

        // ── Main Entry ───────────────────────────────────────────────────
        void GenerateAll()
        {
            EnsureFolders();
            GenerateBullets();
            GenerateVFX();
            GenerateEnemy();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Done!",
                "All pool prefabs created in Assets/Prefabs/Pool/", "OK");
        }

        // ── Folders ──────────────────────────────────────────────────────
        void EnsureFolders()
        {
            foreach (string p in new[] { ROOT, BULLETS, VFX, ENEMIES })
            {
                if (!AssetDatabase.IsValidFolder(p))
                {
                    string parent = Path.GetDirectoryName(p).Replace('\\', '/');
                    string folder = Path.GetFileName(p);
                    AssetDatabase.CreateFolder(parent, folder);
                }
            }
        }

        // ════════════════════════════════════════════════════════════════
        // BULLETS
        // ════════════════════════════════════════════════════════════════
        void GenerateBullets()
        {
            EnsureFolders();
            MakeBullet("Bullet_AR",     0.05f, 0.12f);
            MakeBullet("Bullet_SMG",    0.04f, 0.10f);
            MakeBullet("Bullet_Sniper", 0.03f, 0.15f);
            Debug.Log("[PrefabGen] Bullets done.");
        }

        void MakeBullet(string key, float colliderRadius, float visualScale)
        {
            string path = $"{BULLETS}/{key}.prefab";
            if (Exists(path)) return;

            var go = new GameObject(key);

            // Rigidbody
            var rb                    = go.AddComponent<Rigidbody>();
            rb.useGravity             = false;
            rb.interpolation          = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.constraints            = RigidbodyConstraints.FreezeRotation;

            // Collider
            var col      = go.AddComponent<SphereCollider>();
            col.radius   = colliderRadius;
            col.isTrigger = true;

            // Visual — tiny capsule, no collider
            var vis = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            vis.name = "Visual";
            vis.transform.SetParent(go.transform);
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localRotation = Quaternion.Euler(90, 0, 0);
            vis.transform.localScale    = new Vector3(
                visualScale * 0.4f,
                visualScale * 1.2f,
                visualScale * 0.4f);
            DestroyImmediate(vis.GetComponent<Collider>());

            var mr = vis.GetComponent<MeshRenderer>();
            mr.sharedMaterial = GetOrCreateMaterial("BulletMat", new Color(1f, 0.85f, 0.2f));

            // NOTE: Attach BulletProjectile manually in Inspector after generation
            // (Editor scripts can't reference runtime game scripts safely across assemblies)

            SavePrefab(go, path, key);
        }

        // ════════════════════════════════════════════════════════════════
        // VFX  — NO ParticlePooledObject needed
        //        ParticleSystem.stopAction = Disable handles auto-disable
        //        ObjectPoolManager catches the disable and returns it
        // ════════════════════════════════════════════════════════════════
        void GenerateVFX()
        {
            EnsureFolders();
            MakeVFX("VFX_MuzzleFlash",    0.06f, new Color(1f,   0.70f, 0.20f, 1f), 20, 0.08f);
            MakeVFX("VFX_Impact_Default", 0.15f, new Color(0.8f, 0.70f, 0.50f, 1f), 12, 0.40f);
            MakeVFX("VFX_Impact_Metal",   0.10f, new Color(0.8f, 0.80f, 0.90f, 1f), 15, 0.30f);
            MakeVFX("VFX_Impact_Dirt",    0.20f, new Color(0.6f, 0.45f, 0.30f, 1f), 10, 0.50f);
            MakeVFX("VFX_BloodSplat",     0.20f, new Color(0.7f, 0.05f, 0.05f, 1f), 18, 0.50f);
            Debug.Log("[PrefabGen] VFX done.");
        }

        void MakeVFX(string key, float startSize, Color color, int burst, float lifetime)
        {
            string path = $"{VFX}/{key}.prefab";
            if (Exists(path)) return;

            var go = new GameObject(key);

            // ParticleSystem — no extra script needed
            var ps   = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop          = false;
            main.duration      = lifetime;
            main.startLifetime = lifetime;
            main.startSize     = startSize;
            main.startColor    = color;
            main.maxParticles  = burst * 3;
            main.playOnAwake   = true;

            // ✅ KEY: stopAction = Disable — fires OnDisable on the GameObject
            // ObjectPoolManager.Return() is called from OnDisable via PooledObject
            main.stopAction    = ParticleSystemStopAction.Disable;

            var emission          = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, burst) });

            var fade    = ps.colorOverLifetime;
            fade.enabled = true;
            var grad    = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(1f, 0f),    new GradientAlphaKey(0f, 1f)   });
            fade.color = grad;

            var rend           = go.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = GetOrCreateMaterial($"{key}_Mat", color);
            rend.renderMode     = ParticleSystemRenderMode.Billboard;

            SavePrefab(go, path, key);
        }

        // ════════════════════════════════════════════════════════════════
        // ENEMY
        // ════════════════════════════════════════════════════════════════
        void GenerateEnemy()
        {
            EnsureFolders();
            string path = $"{ENEMIES}/Enemy_Basic.prefab";
            if (Exists(path)) return;

            var go = new GameObject("Enemy_Basic");
            go.tag = "Enemy";

            // Capsule body visual
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(go.transform);
            body.transform.localPosition = new Vector3(0, 1f, 0);
            body.transform.localScale    = new Vector3(0.8f, 1f, 0.8f);
            DestroyImmediate(body.GetComponent<Collider>());
            body.GetComponent<MeshRenderer>().sharedMaterial =
                GetOrCreateMaterial("EnemyMat", new Color(0.3f, 0.5f, 0.3f));

            // Head sphere — assign EnemyHead layer for headshot detection
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(go.transform);
            head.transform.localPosition = new Vector3(0, 1.9f, 0);
            head.transform.localScale    = Vector3.one * 0.45f;
            int headLayer = LayerMask.NameToLayer("EnemyHead");
            if (headLayer >= 0) head.layer = headLayer;
            else Debug.LogWarning("[PrefabGen] EnemyHead layer not found — create it in Tags & Layers!");
            head.GetComponent<MeshRenderer>().sharedMaterial =
                GetOrCreateMaterial("EnemyHeadMat", new Color(0.8f, 0.65f, 0.5f));

            // Capsule Collider on root
            var col    = go.AddComponent<CapsuleCollider>();
            col.height = 1.8f;
            col.radius = 0.4f;
            col.center = new Vector3(0, 0.9f, 0);

            // Rigidbody
            var rb         = go.AddComponent<Rigidbody>();
            rb.constraints = RigidbodyConstraints.FreezeRotationX
                           | RigidbodyConstraints.FreezeRotationZ;

            // NavMeshAgent
            var agent              = go.AddComponent<NavMeshAgent>();
            agent.height           = 1.8f;
            agent.radius           = 0.4f;
            agent.speed            = 3.5f;
            agent.angularSpeed     = 200f;
            agent.stoppingDistance = 4f;

            // FirePoint child
            var fp = new GameObject("FirePoint");
            fp.transform.SetParent(go.transform);
            fp.transform.localPosition = new Vector3(0, 1.5f, 0.6f);

            // Animator only — attach EnemyAI, EnemyHealth, EnemyAnimatorDriver manually
            // (runtime scripts can't be added from Editor scripts across assemblies)
            go.AddComponent<Animator>();

            Debug.Log("[PrefabGen] Enemy_Basic created — manually attach EnemyAI, EnemyHealth, EnemyAnimatorDriver in Inspector.");

            SavePrefab(go, path, "Enemy_Basic");
            Debug.Log("[PrefabGen] Enemy done.");
        }

        // ════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════
        bool Exists(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                Debug.Log($"[PrefabGen] Skipped (exists): {path}");
                return true;
            }
            return false;
        }

        void SavePrefab(GameObject go, string path, string key)
        {
            PrefabUtility.SaveAsPrefabAsset(go, path);
            DestroyImmediate(go);
            Debug.Log($"[PrefabGen] Created: {key} → {path}");
        }

        Material GetOrCreateMaterial(string name, Color color)
        {
            string matDir  = $"{ROOT}/Materials";
            string matPath = $"{matDir}/{name}.mat";

            if (!AssetDatabase.IsValidFolder(matDir))
                AssetDatabase.CreateFolder(ROOT, "Materials");

            var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (existing != null) return existing;

            // Try URP Lit first, fall back to Standard
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Standard");

            var mat = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(mat, matPath);
            return mat;
        }
    }
}
#endif