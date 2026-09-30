using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public sealed class DisplayEnvironmentController : MonoBehaviour
{
    private const float WallpaperSurfaceInset = 0.001f;
    private const string IndoorUnitResourcePath = "DisplayEnvironment/Models/1WayIndoorUnit";
    private const string WindowMeshResourcePath = "DisplayEnvironment/Models/BalconyWindow3Window";
    private const string TelevisionResourcePath = "DisplayEnvironment/Models/Television";
    private const string TelevisionDeskResourcePath = "DisplayEnvironment/Models/TelevisionDesk";
    private const string AirPurifierResourcePath = "DisplayEnvironment/Models/AirPurifier";

    [Header("Physical Layout (metres)")]
    [SerializeField] private Vector3 domainCenter = new Vector3(0f, 1f, 0f);
    [SerializeField] private Vector3 domainSize = new Vector3(5f, 2f, 8f);
    [Min(0.01f)]
    [SerializeField] private float shellThickness = 0.16f;
    [Tooltip("Ceiling contact point centred between the primary inlet and outlet patches.")]
    [SerializeField] private Vector3 indoorUnitCeilingPosition = new Vector3(0f, 2f, 2f);

    [Header("URP Materials")]
    [SerializeField] private Material floorMaterial;
    [SerializeField] private Material wallMaterial;
    [SerializeField] private Material wallInteriorMaterial;
    [SerializeField] private Material ceilingMaterial;
    [SerializeField] private Material indoorUnitMaterial;
    [SerializeField] private Material windowFrameMaterial;
    [SerializeField] private Material windowGlassMaterial;
    [SerializeField] private Material televisionMaterial;
    [SerializeField] private Material televisionDeskMaterial;
    [SerializeField] private Material airPurifierMaterial;
    [SerializeField] private Material airPurifierDarkMaterial;

    [Header("Decor Layout (metres)")]
    [SerializeField] private Vector2 windowOpeningSize = new Vector2(3.6f, 1.9f);
    [SerializeField] private Vector2 windowOpeningCenter = new Vector2(0f, 1f);
    [SerializeField] private Vector3 televisionCenter = new Vector3(2.46f, 1.05f, 0f);
    [Min(0.1f)]
    [SerializeField] private float televisionWidth = 1.25f;
    [Min(0.1f)]
    [SerializeField] private float televisionDeskWidth = 1.55f;
    [SerializeField] private Vector3 airPurifierFloorPosition = new Vector3(2.05f, 0f, -3.45f);
    [Min(0.1f)]
    [SerializeField] private float airPurifierHeight = 0.85f;

    [Header("Initial Cutaway")]
    [SerializeField] private bool showWalls = true;
    [SerializeField] private bool showCameraSideWalls = false;
    [SerializeField] private bool showCeiling = false;
    [SerializeField] private bool showFurniture = true;

    [Header("Status (read-only)")]
    [SerializeField] private bool environmentBuilt;
    [SerializeField] private string lastStatus = "Not built.";

    private Transform wallVisuals;
    private Transform cameraSideWalls;
    private Transform ceilingVisual;
    private Transform optionalFurniture;
    private Mesh runtimeCubeMesh;

    public bool ShowWalls => showWalls;
    public bool ShowCameraSideWalls => showCameraSideWalls;
    public bool ShowCeiling => showCeiling;
    public bool ShowFurniture => showFurniture;
    public bool EnvironmentBuilt => environmentBuilt;
    public string LastStatus => lastStatus;

    private void Awake()
    {
        CaptureAuthoredVisibility();
        BuildEnvironmentIfNeeded();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.f6Key.wasPressedThisFrame)
            ToggleWalls();
        if (keyboard.f7Key.wasPressedThisFrame)
            ToggleCeiling();
        if (keyboard.f8Key.wasPressedThisFrame)
            ToggleCameraSideWalls();
        if (keyboard.f9Key.wasPressedThisFrame)
            ToggleFurniture();
    }

    public void BuildEnvironmentIfNeeded()
    {
        double startedAt = Time.realtimeSinceStartupAsDouble;

        Transform existingFloorSurface = transform.Find("FloorVisual/FloorSurface");
        if (existingFloorSurface != null)
        {
            ResolveGeneratedReferences();
            ApplyVisibility();
            environmentBuilt = true;
            return;
        }

        int visualLayer = LayerMask.NameToLayer("DisplayEnvironment");
        if (visualLayer < 0)
        {
            visualLayer = 0;
            Debug.LogError(
                $"[Display Environment][Case={GetCaseName()}] Layer 'DisplayEnvironment' is missing; using Default.",
                this);
        }

        gameObject.layer = visualLayer;
        runtimeCubeMesh = CreateCubeMesh();

        Transform floor = GetOrCreateNode("FloorVisual", transform, visualLayer);
        CreateBox(
            "FloorSurface",
            floor,
            domainCenter + Vector3.down * (domainSize.y * 0.5f + shellThickness * 0.5f),
            new Vector3(
                domainSize.x + shellThickness * 2f,
                shellThickness,
                domainSize.z + shellThickness * 2f),
            floorMaterial,
            visualLayer);

        wallVisuals = GetOrCreateNode("WallVisuals", transform, visualLayer);
        Transform cutawayWalls = GetOrCreateNode("CutawayWalls", wallVisuals, visualLayer);
        cameraSideWalls = GetOrCreateNode("CameraSideWalls", wallVisuals, visualLayer);

        float halfX = domainSize.x * 0.5f;
        float halfZ = domainSize.z * 0.5f;
        float outerX = halfX + shellThickness * 0.5f;
        float outerZ = halfZ + shellThickness * 0.5f;
        float shellHeight = domainSize.y + shellThickness * 2f;
        Vector3 xWallSize = new Vector3(
            shellThickness,
            shellHeight,
            domainSize.z + shellThickness * 2f);
        Vector3 zWallSize = new Vector3(
            domainSize.x + shellThickness * 2f,
            shellHeight,
            shellThickness);

        CreateBox("WallPositiveX", cutawayWalls, domainCenter + Vector3.right * outerX, xWallSize, wallMaterial, visualLayer);
        CreateWallInteriorSurface(
            "WallpaperPositiveX",
            cutawayWalls,
            new Vector3(domainCenter.x + halfX - WallpaperSurfaceInset, domainCenter.y, domainCenter.z),
            new Vector2(domainSize.z, domainSize.y),
            Vector3.left,
            visualLayer);
        CreatePositiveZWallWithWindow(cutawayWalls, outerZ, zWallSize, visualLayer);
        CreateBox("WallNegativeX", cameraSideWalls, domainCenter + Vector3.left * outerX, xWallSize, wallMaterial, visualLayer);
        CreateWallInteriorSurface(
            "WallpaperNegativeX",
            cameraSideWalls,
            new Vector3(domainCenter.x - halfX + WallpaperSurfaceInset, domainCenter.y, domainCenter.z),
            new Vector2(domainSize.z, domainSize.y),
            Vector3.right,
            visualLayer);
        CreateBox("WallNegativeZ", cameraSideWalls, domainCenter + Vector3.back * outerZ, zWallSize, wallMaterial, visualLayer);
        CreateWallInteriorSurface(
            "WallpaperNegativeZ",
            cameraSideWalls,
            new Vector3(domainCenter.x, domainCenter.y, domainCenter.z - halfZ + WallpaperSurfaceInset),
            new Vector2(domainSize.x, domainSize.y),
            Vector3.forward,
            visualLayer);

        ceilingVisual = GetOrCreateNode("CeilingVisual", transform, visualLayer);
        CreateBox(
            "CeilingSurface",
            ceilingVisual,
            domainCenter + Vector3.up * (domainSize.y * 0.5f + shellThickness * 0.5f),
            new Vector3(
                domainSize.x + shellThickness * 2f,
                shellThickness,
                domainSize.z + shellThickness * 2f),
            ceilingMaterial,
            visualLayer);

        Transform indoorRoot = GetOrCreateNode("IndoorUnitVisual", transform, visualLayer);
        CreateIndoorUnit(indoorRoot, visualLayer);
        optionalFurniture = GetOrCreateNode("OptionalFurniture", transform, visualLayer);
        CreateFurniture(optionalFurniture, visualLayer);

        ApplyVisibility();
        environmentBuilt = true;

        int solverComponentCount = CountSolverComponents();
        int colliderCount = GetComponentsInChildren<Collider>(true).Length;
        int rigidbodyCount = GetComponentsInChildren<Rigidbody>(true).Length;
        double elapsedMs = (Time.realtimeSinceStartupAsDouble - startedAt) * 1000.0;
        lastStatus =
            $"Built in {elapsedMs:F2} ms | layer={LayerMask.LayerToName(visualLayer)} | " +
            $"solverComponents={solverComponentCount} | colliders={colliderCount} | rigidbodies={rigidbodyCount}";

        if (solverComponentCount != 0 || colliderCount != 0 || rigidbodyCount != 0)
            Debug.LogError($"[Display Environment][Case={GetCaseName()}] Visual-only validation failed. {lastStatus}", this);
        else
            Debug.Log($"[Display Environment][Case={GetCaseName()}] {lastStatus}", this);
    }

    [ContextMenu("Toggle Cutaway Walls (F6)")]
    public void ToggleWalls()
    {
        showWalls = !showWalls;
        ApplyVisibility();
        LogVisibility();
    }

    [ContextMenu("Toggle Ceiling (F7)")]
    public void ToggleCeiling()
    {
        showCeiling = !showCeiling;
        ApplyVisibility();
        LogVisibility();
    }

    [ContextMenu("Toggle Camera-side Walls (F8)")]
    public void ToggleCameraSideWalls()
    {
        showCameraSideWalls = !showCameraSideWalls;
        ApplyVisibility();
        LogVisibility();
    }

    [ContextMenu("Toggle Furniture (F9)")]
    public void ToggleFurniture()
    {
        showFurniture = !showFurniture;
        ApplyVisibility();
        LogVisibility();
    }

    public void SetWallsVisible(bool visible)
    {
        showWalls = visible;
        ApplyVisibility();
    }

    public void SetCeilingVisible(bool visible)
    {
        showCeiling = visible;
        ApplyVisibility();
    }

    public void SetCameraSideWallsVisible(bool visible)
    {
        showCameraSideWalls = visible;
        ApplyVisibility();
    }

    public void SetFurnitureVisible(bool visible)
    {
        showFurniture = visible;
        ApplyVisibility();
    }

    private void ResolveGeneratedReferences()
    {
        wallVisuals = transform.Find("WallVisuals");
        cameraSideWalls = transform.Find("WallVisuals/CameraSideWalls");
        ceilingVisual = transform.Find("CeilingVisual");
        optionalFurniture = transform.Find("OptionalFurniture");
    }

    private void CaptureAuthoredVisibility()
    {
        Transform authoredWalls = transform.Find("WallVisuals");
        Transform authoredCameraSideWalls = transform.Find("WallVisuals/CameraSideWalls");
        Transform authoredCeiling = transform.Find("CeilingVisual");
        Transform authoredFurniture = transform.Find("OptionalFurniture");

        if (authoredWalls != null)
            showWalls = authoredWalls.gameObject.activeSelf;
        if (authoredCameraSideWalls != null)
            showCameraSideWalls = authoredCameraSideWalls.gameObject.activeSelf;
        if (authoredCeiling != null)
            showCeiling = authoredCeiling.gameObject.activeSelf;
        if (authoredFurniture != null)
            showFurniture = authoredFurniture.gameObject.activeSelf;
    }

    private void ApplyVisibility()
    {
        if (wallVisuals != null)
            wallVisuals.gameObject.SetActive(showWalls);
        if (cameraSideWalls != null)
            cameraSideWalls.gameObject.SetActive(showWalls && showCameraSideWalls);
        if (ceilingVisual != null)
            ceilingVisual.gameObject.SetActive(showCeiling);
        if (optionalFurniture != null)
            optionalFurniture.gameObject.SetActive(showFurniture);
    }

    private void CreatePositiveZWallWithWindow(
        Transform parent,
        float wallCenterZ,
        Vector3 fallbackWallSize,
        int layer)
    {
        Mesh windowMesh = Resources.Load<Mesh>(WindowMeshResourcePath);
        if (windowMesh == null)
        {
            CreateBox("WallPositiveZ", parent, domainCenter + Vector3.forward * wallCenterZ, fallbackWallSize, wallMaterial, layer);
            CreateWallInteriorSurface(
                "WallpaperPositiveZ",
                parent,
                new Vector3(
                    domainCenter.x,
                    domainCenter.y,
                    domainCenter.z + domainSize.z * 0.5f - WallpaperSurfaceInset),
                new Vector2(domainSize.x, domainSize.y),
                Vector3.back,
                layer);
            Debug.LogWarning(
                $"[Display Environment][Case={GetCaseName()}] Missing window mesh '{WindowMeshResourcePath}'; using a solid +Z wall.",
                this);
            return;
        }

        float roomMinX = domainCenter.x - domainSize.x * 0.5f - shellThickness;
        float roomMaxX = domainCenter.x + domainSize.x * 0.5f + shellThickness;
        float roomMinY = domainCenter.y - domainSize.y * 0.5f - shellThickness;
        float roomMaxY = domainCenter.y + domainSize.y * 0.5f + shellThickness;
        float openingMinX = windowOpeningCenter.x - windowOpeningSize.x * 0.5f;
        float openingMaxX = windowOpeningCenter.x + windowOpeningSize.x * 0.5f;
        float openingMinY = windowOpeningCenter.y - windowOpeningSize.y * 0.5f;
        float openingMaxY = windowOpeningCenter.y + windowOpeningSize.y * 0.5f;
        float overlap = shellThickness * 0.5f;

        CreateWallPanel("WallPositiveZ_Left", parent, roomMinX, openingMinX + overlap, roomMinY, roomMaxY, wallCenterZ, layer);
        CreateWallPanel("WallPositiveZ_Right", parent, openingMaxX - overlap, roomMaxX, roomMinY, roomMaxY, wallCenterZ, layer);
        CreateWallPanel("WallPositiveZ_Bottom", parent, openingMinX - overlap, openingMaxX + overlap, roomMinY, openingMinY + overlap, wallCenterZ, layer);
        CreateWallPanel("WallPositiveZ_Top", parent, openingMinX - overlap, openingMaxX + overlap, openingMaxY - overlap, roomMaxY, wallCenterZ, layer);

        float interiorMinX = domainCenter.x - domainSize.x * 0.5f;
        float interiorMaxX = domainCenter.x + domainSize.x * 0.5f;
        float interiorMinY = domainCenter.y - domainSize.y * 0.5f;
        float interiorMaxY = domainCenter.y + domainSize.y * 0.5f;
        float interiorZ = domainCenter.z + domainSize.z * 0.5f - WallpaperSurfaceInset;
        float clippedOpeningMinX = Mathf.Clamp(openingMinX, interiorMinX, interiorMaxX);
        float clippedOpeningMaxX = Mathf.Clamp(openingMaxX, interiorMinX, interiorMaxX);
        float clippedOpeningMinY = Mathf.Clamp(openingMinY, interiorMinY, interiorMaxY);
        float clippedOpeningMaxY = Mathf.Clamp(openingMaxY, interiorMinY, interiorMaxY);

        CreatePositiveZWallpaperPanel("WallpaperPositiveZ_Left", parent, interiorMinX, clippedOpeningMinX, interiorMinY, interiorMaxY, interiorZ, layer);
        CreatePositiveZWallpaperPanel("WallpaperPositiveZ_Right", parent, clippedOpeningMaxX, interiorMaxX, interiorMinY, interiorMaxY, interiorZ, layer);
        CreatePositiveZWallpaperPanel("WallpaperPositiveZ_Bottom", parent, clippedOpeningMinX, clippedOpeningMaxX, interiorMinY, clippedOpeningMinY, interiorZ, layer);
        CreatePositiveZWallpaperPanel("WallpaperPositiveZ_Top", parent, clippedOpeningMinX, clippedOpeningMaxX, clippedOpeningMaxY, interiorMaxY, interiorZ, layer);

        GameObject window = new GameObject("WindowVisual", typeof(MeshFilter), typeof(MeshRenderer));
        window.layer = layer;
        window.transform.SetParent(parent, false);
        Bounds meshBounds = windowMesh.bounds;
        float scaleX = windowOpeningSize.x / Mathf.Max(meshBounds.size.x, 0.0001f);
        float scaleY = windowOpeningSize.y / Mathf.Max(meshBounds.size.y, 0.0001f);
        float scaleZ = Mathf.Min(1f, shellThickness * 2f / Mathf.Max(meshBounds.size.z, 0.0001f));
        window.transform.localScale = new Vector3(scaleX, scaleY, scaleZ);
        Vector3 scaledCenter = Vector3.Scale(meshBounds.center, window.transform.localScale);
        window.transform.localPosition = new Vector3(
            windowOpeningCenter.x - scaledCenter.x,
            windowOpeningCenter.y - scaledCenter.y,
            domainCenter.z + wallCenterZ - scaledCenter.z);

        window.GetComponent<MeshFilter>().sharedMesh = windowMesh;
        MeshRenderer renderer = window.GetComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { windowFrameMaterial, windowGlassMaterial };
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
    }

    private void CreateWallPanel(
        string objectName,
        Transform parent,
        float minX,
        float maxX,
        float minY,
        float maxY,
        float centerZ,
        int layer)
    {
        CreateBox(
            objectName,
            parent,
            new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, domainCenter.z + centerZ),
            new Vector3(maxX - minX, maxY - minY, shellThickness),
            wallMaterial,
            layer);
    }

    private void CreatePositiveZWallpaperPanel(
        string objectName,
        Transform parent,
        float minX,
        float maxX,
        float minY,
        float maxY,
        float z,
        int layer)
    {
        float width = maxX - minX;
        float height = maxY - minY;
        if (width <= 0.0001f || height <= 0.0001f)
            return;

        CreateWallInteriorSurface(
            objectName,
            parent,
            new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, z),
            new Vector2(width, height),
            Vector3.back,
            layer);
    }

    private void CreateWallInteriorSurface(
        string objectName,
        Transform parent,
        Vector3 localPosition,
        Vector2 size,
        Vector3 inwardNormal,
        int layer)
    {
        if (wallInteriorMaterial == null)
            return;

        GameObject surface = new GameObject(objectName, typeof(MeshFilter), typeof(MeshRenderer));
        surface.layer = layer;
        surface.transform.SetParent(parent, false);
        surface.transform.localPosition = localPosition;
        surface.transform.localRotation = Quaternion.LookRotation(inwardNormal, Vector3.up);

        float halfWidth = size.x * 0.5f;
        float halfHeight = size.y * 0.5f;
        float horizontalRepeats = Mathf.Max(size.x * 0.5f, 0.01f);
        float verticalRepeats = Mathf.Max(size.y, 0.01f);
        Mesh mesh = new Mesh { name = $"{objectName}Mesh" };
        mesh.vertices = new[]
        {
            new Vector3(-halfWidth, -halfHeight, 0f),
            new Vector3(-halfWidth, halfHeight, 0f),
            new Vector3(halfWidth, halfHeight, 0f),
            new Vector3(halfWidth, -halfHeight, 0f)
        };
        mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
        mesh.tangents = new[]
        {
            new Vector4(1f, 0f, 0f, 1f),
            new Vector4(1f, 0f, 0f, 1f),
            new Vector4(1f, 0f, 0f, 1f),
            new Vector4(1f, 0f, 0f, 1f)
        };
        mesh.uv = new[]
        {
            Vector2.zero,
            new Vector2(0f, verticalRepeats),
            new Vector2(horizontalRepeats, verticalRepeats),
            new Vector2(horizontalRepeats, 0f)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
        surface.GetComponent<MeshFilter>().sharedMesh = mesh;

        MeshRenderer renderer = surface.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = wallInteriorMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = true;
    }

    private void CreateFurniture(Transform parent, int layer)
    {
        CreateTelevision(parent, layer);
        CreateAirPurifier(parent, layer);
    }

    private void CreateTelevision(Transform parent, int layer)
    {
        float televisionSupportY = float.NaN;
        if (TryCreateVisualModel(TelevisionDeskResourcePath, "TelevisionDeskVisual", parent, layer, televisionDeskMaterial, null, out GameObject desk, out Renderer[] deskRenderers))
        {
            desk.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            if (TryCalculateRendererBounds(deskRenderers, out Bounds deskBounds))
            {
                float deskScale = televisionDeskWidth / Mathf.Max(deskBounds.size.z, 0.0001f);
                desk.transform.localScale *= deskScale;
                TryCalculateRendererBounds(deskRenderers, out deskBounds);
                desk.transform.position += new Vector3(
                    televisionCenter.x - deskBounds.max.x,
                    -deskBounds.min.y,
                    televisionCenter.z - deskBounds.center.z);
                TryCalculateRendererBounds(deskRenderers, out deskBounds);
                televisionSupportY = deskBounds.max.y;
            }
        }

        if (!TryCreateVisualModel(TelevisionResourcePath, "TelevisionVisual", parent, layer, televisionMaterial, null, out GameObject model, out Renderer[] renderers))
            return;

        model.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
        if (!TryCalculateRendererBounds(renderers, out Bounds bounds))
            return;

        float scale = televisionWidth / Mathf.Max(bounds.size.z, 0.0001f);
        model.transform.localScale *= scale;
        TryCalculateRendererBounds(renderers, out bounds);
        float televisionBottomY = float.IsNaN(televisionSupportY)
            ? televisionCenter.y - bounds.extents.y
            : televisionSupportY + 0.01f;
        model.transform.position += new Vector3(
            televisionCenter.x - bounds.max.x,
            televisionBottomY - bounds.min.y,
            televisionCenter.z - bounds.center.z);
    }

    private void CreateAirPurifier(Transform parent, int layer)
    {
        if (!TryCreateVisualModel(AirPurifierResourcePath, "AirPurifierVisual", parent, layer, airPurifierMaterial, airPurifierDarkMaterial, out GameObject model, out Renderer[] renderers))
            return;

        if (!TryCalculateRendererBounds(renderers, out Bounds bounds))
            return;

        float scale = airPurifierHeight / Mathf.Max(bounds.size.y, 0.0001f);
        model.transform.localScale *= scale;
        TryCalculateRendererBounds(renderers, out bounds);
        model.transform.position += new Vector3(
            airPurifierFloorPosition.x - bounds.center.x,
            airPurifierFloorPosition.y - bounds.min.y,
            airPurifierFloorPosition.z - bounds.center.z);
    }

    private bool TryCreateVisualModel(
        string resourcePath,
        string objectName,
        Transform parent,
        int layer,
        Material primaryMaterial,
        Material darkMaterial,
        out GameObject model,
        out Renderer[] renderers)
    {
        GameObject prefab = Resources.Load<GameObject>(resourcePath);
        if (prefab == null)
        {
            model = null;
            renderers = System.Array.Empty<Renderer>();
            Debug.LogWarning(
                $"[Display Environment][Case={GetCaseName()}] Missing visual model '{resourcePath}'.",
                this);
            return false;
        }

        model = Instantiate(prefab, parent, false);
        model.name = objectName;
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;
        SetLayerRecursively(model.transform, layer);
        RemoveNonVisualComponents(model);

        renderers = model.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                string sourceName = materials[i] != null ? materials[i].name : string.Empty;
                materials[i] = darkMaterial != null && IsDarkPart(sourceName)
                    ? darkMaterial
                    : primaryMaterial;
            }

            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        return true;
    }

    private static bool IsDarkPart(string materialName)
    {
        string normalized = materialName.ToLowerInvariant();
        return normalized.Contains("black")
            || normalized.Contains("dark")
            || normalized.Contains("display")
            || normalized.Contains("grill")
            || normalized.Contains("vent")
            || normalized.Contains("metal");
    }

    private static void RemoveNonVisualComponents(GameObject model)
    {
        foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
            Destroy(collider);
        }

        foreach (Rigidbody body in model.GetComponentsInChildren<Rigidbody>(true))
        {
            body.detectCollisions = false;
            body.isKinematic = true;
            Destroy(body);
        }

        foreach (Animator animator in model.GetComponentsInChildren<Animator>(true))
        {
            animator.enabled = false;
            Destroy(animator);
        }
    }

    private void CreateIndoorUnit(Transform parent, int layer)
    {
        GameObject modelPrefab = Resources.Load<GameObject>(IndoorUnitResourcePath);
        if (modelPrefab == null)
        {
            Debug.LogError(
                $"[Display Environment][Case={GetCaseName()}] Missing Resources model '{IndoorUnitResourcePath}'.",
                this);
            return;
        }

        GameObject model = Instantiate(modelPrefab, parent, false);
        model.name = "OneWayIndoorUnit";
        model.transform.localPosition = indoorUnitCeilingPosition;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;
        SetLayerRecursively(model.transform, layer);

        foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
            Destroy(collider);
        }

        foreach (Rigidbody body in model.GetComponentsInChildren<Rigidbody>(true))
        {
            body.detectCollisions = false;
            body.isKinematic = true;
            Destroy(body);
        }

        foreach (Animator animator in model.GetComponentsInChildren<Animator>(true))
        {
            animator.enabled = false;
            Destroy(animator);
        }

        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (indoorUnitMaterial != null)
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = indoorUnitMaterial;
                renderer.sharedMaterials = materials;
            }

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        if (TryCalculateRendererBounds(renderers, out Bounds bounds))
        {
            Vector3 correction = new Vector3(
                indoorUnitCeilingPosition.x - bounds.center.x,
                indoorUnitCeilingPosition.y - bounds.min.y,
                indoorUnitCeilingPosition.z - bounds.center.z);
            model.transform.position += correction;
        }
    }

    private GameObject CreateBox(
        string objectName,
        Transform parent,
        Vector3 localPosition,
        Vector3 localScale,
        Material material,
        int layer)
    {
        GameObject box = new GameObject(objectName, typeof(MeshFilter), typeof(MeshRenderer));
        box.layer = layer;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = localPosition;
        box.transform.localRotation = Quaternion.identity;
        box.transform.localScale = localScale;

        box.GetComponent<MeshFilter>().sharedMesh = runtimeCubeMesh;
        MeshRenderer renderer = box.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        return box;
    }

    private static Transform CreateNode(string objectName, Transform parent, int layer)
    {
        GameObject node = new GameObject(objectName);
        node.layer = layer;
        node.transform.SetParent(parent, false);
        return node.transform;
    }

    private static Transform GetOrCreateNode(string objectName, Transform parent, int layer)
    {
        Transform existing = parent.Find(objectName);
        if (existing != null)
        {
            SetLayerRecursively(existing, layer);
            return existing;
        }

        return CreateNode(objectName, parent, layer);
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }

    private static bool TryCalculateRendererBounds(Renderer[] renderers, out Bounds bounds)
    {
        bounds = default;
        bool initialized = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            if (!initialized)
            {
                bounds = renderer.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return initialized;
    }

    private int CountSolverComponents()
    {
        return GetComponentsInChildren<DeviceObstacles>(true).Length
            + GetComponentsInChildren<Racks>(true).Length
            + GetComponentsInChildren<ACSource>(true).Length
            + GetComponentsInChildren<LBMZouHeBox>(true).Length;
    }

    private void LogVisibility()
    {
        Debug.Log(
            $"[Display Environment][Case={GetCaseName()}] walls={showWalls}, " +
            $"cameraSideWalls={showCameraSideWalls}, ceiling={showCeiling}",
            this);
    }

    private static string GetCaseName()
    {
        SimulationController controller = SimulationController.Instance;
        return controller != null && !string.IsNullOrWhiteSpace(controller.ActiveCaseName)
            ? controller.ActiveCaseName
            : "Manual";
    }

    private static Mesh CreateCubeMesh()
    {
        var mesh = new Mesh { name = "DisplayEnvironmentCube" };
        Vector3[] vertices =
        {
            new Vector3(-.5f, -.5f, -.5f), new Vector3(.5f, -.5f, -.5f), new Vector3(.5f, .5f, -.5f), new Vector3(-.5f, .5f, -.5f),
            new Vector3(.5f, -.5f, .5f), new Vector3(-.5f, -.5f, .5f), new Vector3(-.5f, .5f, .5f), new Vector3(.5f, .5f, .5f),
            new Vector3(-.5f, -.5f, .5f), new Vector3(-.5f, -.5f, -.5f), new Vector3(-.5f, .5f, -.5f), new Vector3(-.5f, .5f, .5f),
            new Vector3(.5f, -.5f, -.5f), new Vector3(.5f, -.5f, .5f), new Vector3(.5f, .5f, .5f), new Vector3(.5f, .5f, -.5f),
            new Vector3(-.5f, .5f, -.5f), new Vector3(.5f, .5f, -.5f), new Vector3(.5f, .5f, .5f), new Vector3(-.5f, .5f, .5f),
            new Vector3(-.5f, -.5f, .5f), new Vector3(.5f, -.5f, .5f), new Vector3(.5f, -.5f, -.5f), new Vector3(-.5f, -.5f, -.5f)
        };
        int[] triangles =
        {
            0, 2, 1, 0, 3, 2,
            4, 6, 5, 4, 7, 6,
            8, 10, 9, 8, 11, 10,
            12, 14, 13, 12, 15, 14,
            16, 18, 17, 16, 19, 18,
            20, 22, 21, 20, 23, 22
        };
        Vector2[] faceUvs = { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        Vector2[] uvs = new Vector2[24];
        for (int face = 0; face < 6; face++)
            for (int vertex = 0; vertex < 4; vertex++)
                uvs[face * 4 + vertex] = faceUvs[vertex];

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void OnDestroy()
    {
        if (runtimeCubeMesh != null)
            Destroy(runtimeCubeMesh);
    }

    private void OnValidate()
    {
        domainSize = new Vector3(
            Mathf.Max(0.01f, domainSize.x),
            Mathf.Max(0.01f, domainSize.y),
            Mathf.Max(0.01f, domainSize.z));
        shellThickness = Mathf.Max(0.01f, shellThickness);
        televisionDeskWidth = Mathf.Max(0.1f, televisionDeskWidth);
    }
}
