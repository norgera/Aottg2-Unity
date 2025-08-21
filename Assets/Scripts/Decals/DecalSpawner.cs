using System.Collections.Generic;
using ApplicationManagers;
using GameManagers;
using Photon.Pun;
using Settings;
using UnityEngine;
using Utility;
using CustomSkins;

namespace Decals
{
    public enum DecalType
    {
        Generic = 0,
        Spray = 1,
        SolidSpray = 2,
    }

    class DecalSpawner : MonoBehaviour
    {
        public static DecalSpawner instance;
        
        private static readonly List<DecalInstance> ActiveDecals = new List<DecalInstance>();

        // Cached materials to avoid shader compilation on each spawn
        private static Material _cachedSteveDecalMaterial;
        private static Material _cachedSprayMaterial;
        private static Material _cachedSolidSprayMaterial;

        // Cached textures to avoid loading on each spawn
        private static readonly Dictionary<string, Texture2D> _cachedTextures = new Dictionary<string, Texture2D>();

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (instance != this)
            {
                Destroy(gameObject);
                return;
            }
        }

        private void Start()
        {
            InitializeCachedMaterials();
        }

        public static void Unregister(DecalInstance instance)
        {
            if (instance == null)
                return;
            ActiveDecals.Remove(instance);
        }

        /// <summary>
        /// Pre-initialize cached materials to avoid shader compilation during first decal spawn.
        /// Called by InGameManager during startup.
        /// </summary>
        public static void InitializeCachedMaterials()
        {
            var steveShader = Shader.Find("GAPH Custom Shader/Shader_Decal");
            var sprayShader = Shader.Find("GAPH Custom Shader/Shader_Decal_Spray");
            var solidSprayShader = Shader.Find("GAPH Custom Shader/Shader_Decal_Spray_Solid");

            // Pre-create cached materials so they're ready for use
            if (steveShader != null && _cachedSteveDecalMaterial == null)
            {
                GetOrCreateCachedMaterial(steveShader, DecalType.Generic);
            }
            if (sprayShader != null && _cachedSprayMaterial == null)
            {
                GetOrCreateCachedMaterial(sprayShader, DecalType.Spray);
            }
            if (solidSprayShader != null && _cachedSolidSprayMaterial == null)
            {
                GetOrCreateCachedMaterial(solidSprayShader, DecalType.SolidSpray);
            }
            
            // Pre-cache common textures to avoid loading lag
            PreCacheCommonTextures();
            
            Debug.Log("[DecalSpawner] Cached materials pre-initialized");
        }

        private static void PreCacheCommonTextures()
        {
            // Cache the most commonly used bloodsplat texture
            string[] commonTextures = new string[]
            {
                "SteveRandomGarbage/bloodsplat1",
                "SteveRandomGarbage/cut1", 
            };

            foreach (string texturePath in commonTextures)
            {
                CacheTexture(texturePath);
            }
        }

        private static void CacheTexture(string idOrUrl)
        {
            if (_cachedTextures.ContainsKey(idOrUrl))
                return;

            Texture2D tex = null;
            
            // Try Resources first
            var res = Resources.Load<Texture2D>(idOrUrl);
            if (res != null)
            {
                tex = res;
            }
            else if (Resources.Load<Sprite>(idOrUrl) is Sprite spr && spr.texture != null)
            {
                tex = spr.texture;
            }
            else
            {
                // Try to load directly from Assets for dev
                string basePath = Application.dataPath;
                string pathPng = System.IO.Path.Combine(basePath, idOrUrl + ".png");
                string pathJpg = System.IO.Path.Combine(basePath, idOrUrl + ".jpg");
                
                if (System.IO.File.Exists(pathPng))
                    tex = ResourceManager.LoadExternalTexture(pathPng, idOrUrl);
                else if (System.IO.File.Exists(pathJpg))
                    tex = ResourceManager.LoadExternalTexture(pathJpg, idOrUrl);
            }

            if (tex != null)
            {
                _cachedTextures[idOrUrl] = tex;
                Debug.Log($"[DecalSpawner] Pre-cached texture: {idOrUrl}");
            }
        }

        private static Material GetOrCreateCachedMaterial(Shader shader, DecalType decalType)
        {
            Material cachedMaterial = null;
            
            // Get cached material based on decal type
            switch (decalType)
            {
                case DecalType.Generic:
                    cachedMaterial = _cachedSteveDecalMaterial;
                    break;
                case DecalType.Spray:
                    cachedMaterial = _cachedSprayMaterial;
                    break;
                case DecalType.SolidSpray:
                    cachedMaterial = _cachedSolidSprayMaterial;
                    break;
            }

            // Create cached material if it doesn't exist
            if (cachedMaterial == null && shader != null)
            {
                cachedMaterial = new Material(shader);
                cachedMaterial.enableInstancing = true;
                
                // Ensure no shader-side animation/distortion and reset offsets
                cachedMaterial.DisableKeyword("IS_TEXTURE_ANIMATE");
                cachedMaterial.DisableKeyword("IS_NORMAL_DISTORTION");
                cachedMaterial.DisableKeyword("IS_NORMAL_ANIMATE");
                cachedMaterial.SetFloat("_TextureAnimateSpeed", 0f);
                cachedMaterial.SetFloat("_NormalAnimateSpeed", 0f);
                cachedMaterial.SetFloat("_NormalDistortionFactor", 0f);
                cachedMaterial.mainTextureOffset = Vector2.zero;
                cachedMaterial.mainTextureScale = Vector2.one;
                
                // Use standard alpha blending so decals do not blow out brightness (avoid additive by default)
                cachedMaterial.SetFloat("_BlendSrc", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                cachedMaterial.SetFloat("_BlendDst", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                
                // Slightly reduce overall tint alpha to soften stacking
                if (cachedMaterial.HasProperty("_TintColor"))
                {
                    var tint = cachedMaterial.GetColor("_TintColor");
                    tint.a = Mathf.Clamp01(tint.a * 0.9f);
                    cachedMaterial.SetColor("_TintColor", tint);
                }

                // Cache the material
                switch (decalType)
                {
                    case DecalType.Generic:
                        _cachedSteveDecalMaterial = cachedMaterial;
                        break;
                    case DecalType.Spray:
                        _cachedSprayMaterial = cachedMaterial;
                        break;
                    case DecalType.SolidSpray:
                        _cachedSolidSprayMaterial = cachedMaterial;
                        break;
                }
                
                Debug.Log($"[Decals] Created cached template material for {decalType} using shader: {shader.name}");
            }

            return cachedMaterial;
        }

        private static Material CreateMaterialFromTemplate(Material template)
        {
            if (template == null) return null;
            
            // Create a new material instance from the template
            // This copies all properties without triggering shader compilation
            return new Material(template);
        }

        public static void SpawnNetworked(DecalType type, string idOrUrl, Vector3 position, Vector3 normal, float size, float lifetime, bool replaceExisting)
        {
            int owner = PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1;
            RPCManager.PhotonView.RPC(
                "SpawnDecalRPC",
                RpcTarget.All,
                new object[]
                {
                    (int)type,
                    idOrUrl,
                    position,
                    normal,
                    size,
                    lifetime,
                    owner,
                    replaceExisting
                }
            );
        }

        public static void SpawnNetworkedOriented(DecalType type, string idOrUrl, Vector3 position, Vector3 normal, Vector3 surfaceForward, float size, float lifetime, bool replaceExisting)
        {
            int owner = PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1;
            RPCManager.PhotonView.RPC(
                "SpawnDecalOrientedRPC",
                RpcTarget.All,
                new object[]
                {
                    (int)type,
                    idOrUrl,
                    position,
                    normal,
                    surfaceForward,
                    size,
                    lifetime,
                    owner,
                    replaceExisting
                }
            );
        }

        public static void OnSpawnDecalRPC(int type, string idOrUrl, Vector3 position, Vector3 normal, float size, float lifetime, int ownerActorNumber, bool replaceExisting, PhotonMessageInfo info)
        {
            var decalType = (DecalType)type;

            Debug.Log($"[Decals] RPC spawn request -> type={decalType} idOrUrl={idOrUrl} pos={position} normal={normal} size={size} life={lifetime}s owner={ownerActorNumber} replace={replaceExisting}");

            // Enforce per-player spray uniqueness
            if ((decalType == DecalType.Spray || decalType == DecalType.SolidSpray) && replaceExisting)
            {
                for (int i = ActiveDecals.Count - 1; i >= 0; i--)
                {
                    var d = ActiveDecals[i];
                    if (d != null && (d.Type == DecalType.Spray || d.Type == DecalType.SolidSpray) && d.OwnerActorNumber == ownerActorNumber)
                    {
                        Object.Destroy(d.gameObject);
                        ActiveDecals.RemoveAt(i);
                    }
                }
            }

            // Enforce global max decals for generic decals only
            if (decalType == DecalType.Generic)
            {
                int maxDecals = Mathf.Max(0, SettingsManager.GraphicsSettings.DecalMaxCount.Value);
                if (maxDecals > 0 && CountByType(DecalType.Generic) >= maxDecals)
                {
                    // Remove oldest generic
                    RemoveOldestOfType(DecalType.Generic);
                }
                // Enforce per-player cap for non-host
                if (!info.Sender.IsMasterClient)
                {
                    int perPlayerMax = Mathf.Max(1, SettingsManager.GraphicsSettings.DecalPerPlayerMax.Value);
                    if (CountByTypeAndOwner(DecalType.Generic, ownerActorNumber) >= perPlayerMax)
                    {
                        RemoveOldestOfTypeAndOwner(DecalType.Generic, ownerActorNumber);
                    }
                }
            }
            else if (decalType == DecalType.Spray || decalType == DecalType.SolidSpray)
            {
                // Enforce per-player max sprays (host can be exempt by setting a high limit externally if needed)
                int perPlayerMax = Mathf.Max(1, SettingsManager.GraphicsSettings.DecalPerPlayerMax.Value);
                if (CountByTypeAndOwner(decalType, ownerActorNumber) >= perPlayerMax)
                {
                    RemoveOldestOfTypeAndOwner(decalType, ownerActorNumber);
                }
            }

            // Create decal object
            var go = new GameObject("DecalInstance");
            _lastCreatedDecal = go;
            go.transform.position = position + normal.normalized * 0.02f; // offset to avoid z-fighting
            // Default alignment assumes projector shader (local +Y is projection axis). For quad fallback we'll correct below.
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal.normalized);
            var instance = go.AddComponent<DecalInstance>();
            instance.Initialize(decalType, ownerActorNumber, lifetime, size);

            // Add a simple collider for debugging visibility
            var debugCollider = go.AddComponent<BoxCollider>();
            debugCollider.isTrigger = true;
            debugCollider.size = Vector3.one * 0.1f;
            
            Debug.Log($"[Decals] Created decal at position: {go.transform.position}, rotation: {go.transform.rotation.eulerAngles}");

            // Visual: projector cube using Steve's decal shader if present, else fallback unlit quad
            var meshFilter = go.AddComponent<MeshFilter>();
            var meshRenderer = go.AddComponent<MeshRenderer>();
            var steveShader = Shader.Find("GAPH Custom Shader/Shader_Decal");
            var sprayShader = Shader.Find("GAPH Custom Shader/Shader_Decal_Spray");
            var solidSprayShader = Shader.Find("GAPH Custom Shader/Shader_Decal_Spray_Solid");
            Material mat;
            // Use cube projector for Generic decals (Steve) and also for Sprays if the spray projector shader exists.
            // The spray shader relies on cube projection math and won't render correctly on a simple quad.
            bool useSteveProjector = steveShader != null && decalType == DecalType.Generic;
            bool useSprayProjector = sprayShader != null && decalType == DecalType.Spray;
            bool useSolidSprayProjector = solidSprayShader != null && decalType == DecalType.SolidSpray;
            bool useProjector = useSteveProjector || useSprayProjector || useSolidSprayProjector;
            if (useProjector)
            {
                meshFilter.sharedMesh = CreateCubeMesh();
                Shader selectedShader = useSolidSprayProjector ? solidSprayShader : (useSprayProjector ? sprayShader : steveShader);
                
                // Get cached template material and create instance from it
                Material templateMaterial = GetOrCreateCachedMaterial(selectedShader, decalType);
                mat = CreateMaterialFromTemplate(templateMaterial);
                if (mat == null)
                {
                    Debug.LogError($"[Decals] Failed to create material for {decalType}");
                    return;
                }
                
                meshRenderer.sharedMaterial = mat;
                // Set projector volume (cube of size). Use full depth so it can span nearby objects.
                go.transform.localScale = new Vector3(size, size, size);
                
                // Force material to visible render queue for debugging
                if (mat.renderQueue < 2000) // If it's not already transparent
                {
                    mat.renderQueue = 2500; // Geometry queue
                    Debug.Log($"[Decals] Adjusted render queue to: {mat.renderQueue}");
                }
                
                var projector = go.AddComponent<DecalProjector>();
                projector.Apply(meshRenderer);
                string shaderName = selectedShader != null ? selectedShader.name : "null";
                Debug.Log($"[Decals] Using cube projector with material from template, shader: {shaderName} (DecalType: {decalType})");
            }
            else
            {
                meshFilter.sharedMesh = CreateQuadMesh();
                // Fallback (no projector shaders): transparent quad
                mat = CreateDecalMaterial();
                meshRenderer.sharedMaterial = mat;
                meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
                if (meshRenderer.sharedMaterial != null)
                    meshRenderer.sharedMaterial.renderQueue = 3000; // Transparent
                // Scale X/Y in world units; keep Z as a tiny thickness so the quad sits flush on the surface
                go.transform.localScale = new Vector3(size, size, 0.001f);
                Debug.Log("[Decals] Using Unlit quad for decal");
            }

            // Record geometry for aspect correction after texture loads
            var shape = go.AddComponent<DecalShape>();
            shape.Geometry = useProjector ? DecalGeometry.CubeProjector : DecalGeometry.Quad;
            // If using a simple quad, make the quad face outward along the surface normal (local +Z = normal)
            if (shape.Geometry == DecalGeometry.Quad)
                go.transform.rotation = Quaternion.LookRotation(normal.normalized, Vector3.up);
            shape.BaseSize = size;

            // Load texture
            if (instance != null)
                instance.StartCoroutine(LoadTextureAndApply(go, mat, idOrUrl));
            else
                go.AddComponent<MonoBehaviourProxy>().StartCoroutine(LoadTextureAndApply(go, mat, idOrUrl));

            ActiveDecals.Add(instance);

            // Add simple culling based on distance
            var culling = go.AddComponent<DecalCulling>();
            culling.Init(SettingsManager.GraphicsSettings.DecalRenderDistance.Value);
        }

        public static void OnSpawnDecalOrientedRPC(int type, string idOrUrl, Vector3 position, Vector3 normal, Vector3 surfaceUpProjected, float size, float lifetime, int ownerActorNumber, bool replaceExisting, PhotonMessageInfo info)
        {
            // Step 1: spawn with up aligned to normal (handled in OnSpawnDecalRPC)
            Vector3 n = normal.normalized;
            OnSpawnDecalRPC(type, idOrUrl, position, n, size, lifetime, ownerActorNumber, replaceExisting, info);
            if (_lastCreatedDecal != null)
            {
                // Step 2: rotate around up (normal) so the local forward (Z) aligns with camera-up projected on the plane
                var tr = _lastCreatedDecal.transform;
                Vector3 currentZ = tr.forward - Vector3.Dot(tr.forward, n) * n;
                if (currentZ.sqrMagnitude < 1e-6f)
                    currentZ = Vector3.Cross(n, Vector3.right);
                else
                    currentZ.Normalize();

                Vector3 desiredZ = surfaceUpProjected - Vector3.Dot(surfaceUpProjected, n) * n;
                if (desiredZ.sqrMagnitude < 1e-6f)
                    desiredZ = currentZ;
                else
                    desiredZ.Normalize();

                float angle = Vector3.SignedAngle(currentZ, desiredZ, n);
                tr.Rotate(n, angle, Space.World);
            }
        }

        public static void SpawnNetworkedAttach(DecalType type, string idOrUrl, Vector3 position, Vector3 normal, float size, float lifetime, bool replaceExisting, PhotonView parentView, Transform parentTransform)
        {
            int owner = PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1;
            string path = GetHierarchyPath(parentTransform, parentView.transform);
            RPCManager.PhotonView.RPC(
                "SpawnDecalAttachRPC",
                RpcTarget.All,
                new object[]
                {
                    (int)type, idOrUrl, position, normal, size, lifetime, owner, replaceExisting,
                    parentView.ViewID,
                    path
                }
            );
        }

        public static void OnSpawnDecalAttachRPC(int type, string idOrUrl, Vector3 position, Vector3 normal, float size, float lifetime, int ownerActorNumber, bool replaceExisting, int parentViewId, string parentPath, PhotonMessageInfo info)
        {
            var view = PhotonView.Find(parentViewId);
            if (view == null)
            {
                Debug.LogWarning($"[Decals] Parent view {parentViewId} not found for attached decal");
                return;
            }
            Transform parent = ResolveHierarchyPath(view.transform, parentPath);
            if (parent == null)
            {
                Debug.LogWarning($"[Decals] Parent path '{parentPath}' not found under view {parentViewId}");
                parent = view.transform;
            }
            // Spawn decal normally, then parent and convert world to local
            OnSpawnDecalRPC(type, idOrUrl, position, normal, size, lifetime, ownerActorNumber, replaceExisting, info);
            var go = FindLatestDecalGameObject();
            if (go != null)
            {
                go.transform.SetParent(parent, true);
                Debug.Log($"[Decals] Attached decal under {parent.name} path={parentPath}");
                // Bias projector inward so it wraps around titan geometry and avoids intersecting nearby ground.
                // Up axis is aligned to the surface normal in OnSpawnDecalRPC.
                Vector3 n = normal.normalized;
                var shape = go.GetComponent<DecalShape>();
                float baseSize = shape != null ? shape.BaseSize : size;
                // Maintain a small constant outward thickness regardless of decal size.
                float outwardThickness = 0.03f;
                float pushIn = Mathf.Max(0f, (baseSize * 0.5f) - outwardThickness);
                go.transform.position += -n * pushIn;
            }
        }

        private static string GetHierarchyPath(Transform child, Transform root)
        {
            if (child == null || root == null)
                return string.Empty;
            var segments = new System.Collections.Generic.List<string>();
            Transform t = child;
            while (t != null && t != root)
            {
                segments.Add(t.name);
                t = t.parent;
            }
            segments.Reverse();
            return string.Join("/", segments);
        }

        private static Transform ResolveHierarchyPath(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path))
                return root;
            Transform current = root;
            foreach (var seg in path.Split('/'))
            {
                current = current.Find(seg);
                if (current == null)
                    return null;
            }
            return current;
        }

        private static GameObject FindLatestDecalGameObject()
        {
            // Return reference captured at create time
            return _lastCreatedDecal;
        }

        private static GameObject _lastCreatedDecal;

        private static int CountByType(DecalType type)
        {
            int count = 0;
            foreach (var d in ActiveDecals)
            {
                if (d != null && d.Type == type)
                    count++;
            }
            return count;
        }

        private static void RemoveOldestOfType(DecalType type)
        {
            DecalInstance oldest = null;
            foreach (var d in ActiveDecals)
            {
                if (d != null && d.Type == type)
                {
                    if (oldest == null || d.SpawnTime < oldest.SpawnTime)
                        oldest = d;
                }
            }
            if (oldest != null)
            {
                Object.Destroy(oldest.gameObject);
                ActiveDecals.Remove(oldest);
            }
        }

        private static int CountByTypeAndOwner(DecalType type, int owner)
        {
            int count = 0;
            foreach (var d in ActiveDecals)
            {
                if (d != null && d.Type == type && d.OwnerActorNumber == owner)
                    count++;
            }
            return count;
        }

        private static void RemoveOldestOfTypeAndOwner(DecalType type, int owner)
        {
            DecalInstance oldest = null;
            foreach (var d in ActiveDecals)
            {
                if (d != null && d.Type == type && d.OwnerActorNumber == owner)
                {
                    if (oldest == null || d.SpawnTime < oldest.SpawnTime)
                        oldest = d;
                }
            }
            if (oldest != null)
            {
                Object.Destroy(oldest.gameObject);
                ActiveDecals.Remove(oldest);
            }
        }

        private static Mesh CreateQuadMesh()
        {
            var mesh = new Mesh();
            mesh.name = "DecalQuad";
            mesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
            };
            mesh.uv = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
            };
            mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateNormals();
            return mesh;
        }

        private static Material CreateDecalMaterial()
        {
            Shader shader = Shader.Find("Unlit/Texture");
            if (shader == null)
                shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            mat.color = Color.white;
            // Disable any scaling of texture tiling so original image dimensions are preserved by default
            mat.mainTextureScale = Vector2.one;
            mat.mainTextureOffset = Vector2.zero;
            return mat;
        }

        private static Mesh _cachedCubeMesh;
        private static Mesh CreateCubeMesh()
        {
            if (_cachedCubeMesh != null)
                return _cachedCubeMesh;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cachedCubeMesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            return _cachedCubeMesh;
        }

        private static System.Collections.IEnumerator LoadTextureAndApply(GameObject go, Material mat, string idOrUrl)
        {
            // First check if we have a cached texture
            if (_cachedTextures.TryGetValue(idOrUrl, out Texture2D cachedTex))
            {
                Debug.Log($"[Decals] Using cached texture: '{idOrUrl}'");
                ApplyTextureToDecal(mat, go, cachedTex);
                yield break;
            }

            // Support either a Resources path, a known background alias, or a URL
            Texture2D tex = null;
            Texture2D normal = null;
            // Map BG:<name> alias to Resources UI/Backgrounds/<name> (search both root and common subfolders)
            if (idOrUrl.StartsWith("BG:"))
            {
                string bgName = idOrUrl.Substring(3);
                // Try root path first
                string candidate = "UI/Backgrounds/" + bgName;
                if (Resources.Load<Texture2D>(candidate) != null || Resources.Load<Sprite>(candidate) != null)
                {
                    idOrUrl = candidate;
                }
                else
                {
                    // Known subfolders
                    string[] subfolders = new[] { "Blood" };
                    bool resolved = false;
                    foreach (var sub in subfolders)
                    {
                        candidate = $"UI/Backgrounds/{sub}/" + bgName;
                        if (Resources.Load<Texture2D>(candidate) != null || Resources.Load<Sprite>(candidate) != null)
                        {
                            idOrUrl = candidate;
                            resolved = true;
                            break;
                        }
                    }
                    if (!resolved)
                    {
                        // keep idOrUrl as name and fall back to name-based search later
                        idOrUrl = "UI/Backgrounds/" + bgName;
                    }
                }
            }
            // Try Resources first (Texture2D)
            var res = Resources.Load<Texture2D>(idOrUrl);
            if (res != null)
            {
                tex = res;
                // Heuristic: if a Normal map named like Normal_4 exists in same folder, try load
                string foldered = idOrUrl;
                var normalRes = Resources.Load<Texture2D>(foldered.Replace("Blood", "Normal_4"));
                if (normalRes != null) normal = normalRes;
                Debug.Log($"[Decals] Loaded texture from Resources: '{idOrUrl}' normal={(normal!=null)}");
            }
            else if (Resources.Load<Sprite>(idOrUrl) is Sprite spr && spr.texture != null)
            {
                tex = spr.texture;
                Debug.Log($"[Decals] Loaded sprite from Resources: '{idOrUrl}' -> using sprite.texture");
            }
            else
            {
                // Fallback: resolve by name within common UI folders (handles subfolders like UI/Backgrounds/Blood/*)
                string nameOnly = System.IO.Path.GetFileName(idOrUrl);
                if (!string.IsNullOrEmpty(nameOnly))
                {
                    // Search sprites first in Backgrounds then UI
                    if (tex == null)
                    {
                        var sprites = Resources.LoadAll<Sprite>("UI/Backgrounds");
                        foreach (var s in sprites)
                        {
                            if (s != null && s.name == nameOnly && s.texture != null)
                            {
                                tex = s.texture;
                                Debug.Log($"[Decals] Resolved sprite by name in UI/Backgrounds: '{nameOnly}'");
                                break;
                            }
                        }
                    }
                    if (tex == null)
                    {
                        var sprites = Resources.LoadAll<Sprite>("UI");
                        foreach (var s in sprites)
                        {
                            if (s != null && s.name == nameOnly && s.texture != null)
                            {
                                tex = s.texture;
                                Debug.Log($"[Decals] Resolved sprite by name in UI: '{nameOnly}'");
                                break;
                            }
                        }
                    }
                    // Then textures
                    if (tex == null)
                    {
                        var texs = Resources.LoadAll<Texture2D>("UI/Backgrounds");
                        foreach (var t in texs)
                        {
                            if (t != null && t.name == nameOnly)
                            {
                                tex = t;
                                Debug.Log($"[Decals] Resolved texture by name in UI/Backgrounds: '{nameOnly}'");
                                break;
                            }
                        }
                    }
                    if (tex == null)
                    {
                        var texs = Resources.LoadAll<Texture2D>("UI");
                        foreach (var t in texs)
                        {
                            if (t != null && t.name == nameOnly)
                            {
                                tex = t;
                                Debug.Log($"[Decals] Resolved texture by name in UI: '{nameOnly}'");
                                break;
                            }
                        }
                    }
                }
            }
            if (tex == null && TextureDownloader.ValidTextureURL(idOrUrl))
            {
                bool mipmap = SettingsManager.GraphicsSettings.MipmapEnabled.Value;
                var proxy = go.GetComponent<MonoBehaviourProxy>();
                CoroutineWithData cwd = new CoroutineWithData(proxy, TextureDownloader.DownloadTexture(proxy, idOrUrl, mipmap, 10 * 1024 * 1024));
                yield return cwd.Coroutine;
                tex = cwd.Result as Texture2D;
                Debug.Log($"[Decals] Downloaded texture from URL: success={(tex!=null)} url='{idOrUrl}'");
            }
            else if (tex == null)
            {
                // Try to load directly from Assets for dev (not for release) using ResourceManager external loader
                string basePath = Application.dataPath;
                string pathPng = System.IO.Path.Combine(basePath, idOrUrl + ".png");
                string pathJpg = System.IO.Path.Combine(basePath, idOrUrl + ".jpg");
                Texture2D loaded = null;
                if (System.IO.File.Exists(pathPng))
                    loaded = ResourceManager.LoadExternalTexture(pathPng, idOrUrl);
                else if (System.IO.File.Exists(pathJpg))
                    loaded = ResourceManager.LoadExternalTexture(pathJpg, idOrUrl);
                if (loaded != null)
                {
                    tex = loaded;
                    Debug.Log($"[Decals] Loaded texture from Assets path: '{(System.IO.File.Exists(pathPng) ? pathPng : pathJpg)}'");
                }
                else
                {
                    Debug.LogWarning($"[Decals] Texture not found in Resources and URL not valid: '{idOrUrl}'. Using material color only.");
                }
            }

            if (tex != null)
            {
                // Cache the texture for future use (only cache successfully loaded textures)
                if (!_cachedTextures.ContainsKey(idOrUrl))
                {
                    _cachedTextures[idOrUrl] = tex;
                    Debug.Log($"[Decals] Cached texture for future use: '{idOrUrl}'");
                }
                
                ApplyTextureToDecal(mat, go, tex, normal);
            }
        }

        private static void ApplyTextureToDecal(Material mat, GameObject go, Texture2D tex, Texture2D normal = null)
        {
            mat.mainTexture = tex;
            if (normal != null && mat.HasProperty("_NormalTex"))
                mat.SetTexture("_NormalTex", normal);
            Debug.Log($"[Decals] Applied textures. main={(tex!=null)} normal={(normal!=null)}");
            Debug.Log($"[Decals] Texture dimensions: {tex.width}x{tex.height}, aspect={((float)tex.width/(float)tex.height):F2}");
            
            // Ensure material texture scaling is properly reset for aspect ratio calculations
            mat.mainTextureScale = Vector2.one;
            mat.mainTextureOffset = Vector2.zero;
            
            // Maintain original aspect ratio
            var shape = go.GetComponent<DecalShape>();
            Debug.Log($"[Decals] Shape component: {(shape != null ? "found" : "null")}, tex dimensions valid: {(tex.width > 0 && tex.height > 0)}");
            if (shape != null)
                Debug.Log($"[Decals] Shape.Geometry: {shape.Geometry}, Shape.BaseSize: {shape.BaseSize}");
            
            if (shape != null && tex.width > 0 && tex.height > 0)
            {
                float aspect = (float)tex.width / (float)tex.height; // width/height
                Debug.Log($"[Decals] Aspect ratio calculation: {aspect:F2}, geometry={shape.Geometry}");
                if (shape.Geometry == DecalGeometry.CubeProjector)
                {
                    float baseSize = shape.BaseSize;
                    // For cube projectors, texture maps using XZ coordinates (opos.xz in shader)
                    // So width=X-axis, height=Z-axis for proper aspect ratio
                    float width, depth;
                    if (aspect >= 1.0f) // wider than tall
                    {
                        width = baseSize;
                        depth = baseSize / aspect;
                    }
                    else // taller than wide
                    {
                        width = baseSize * aspect;
                        depth = baseSize;
                    }
                    Debug.Log($"[Decals] CubeProjector scale: width={width:F2}, baseSize={baseSize:F2}, depth={depth:F2}");
                    // X=width(horizontal), Y=projection height, Z=depth(vertical texture axis)
                    go.transform.localScale = new Vector3(width, baseSize, depth);
                }
                else
                {
                    float baseSize = shape.BaseSize;
                    // Same logic for quad geometry
                    float width, height;
                    if (aspect >= 1.0f) // wider than tall
                    {
                        width = baseSize;
                        height = baseSize / aspect;
                    }
                    else // taller than wide
                    {
                        width = baseSize * aspect;
                        height = baseSize;
                    }
                    go.transform.localScale = new Vector3(width, height, 1f);
                }
            }
        }
    }
}
