using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class DisplayOrbitCameraController : MonoBehaviour
{
    private enum DragMode
    {
        None,
        Orbit,
        Pan
    }

    [Header("References")]
    [SerializeField] private RectTransform inputArea;
    [SerializeField] private Transform domainRoot;

    [Header("Mouse Controls")]
    [SerializeField, Min(1f)] private float orbitDegreesPerViewport = 180f;
    [SerializeField, Min(0.01f)] private float panSpeedMultiplier = 1f;
    [SerializeField, Range(0.01f, 1f)] private float zoomSensitivity = 0.75f;
    [SerializeField] private bool enableRKeyReset = true;

    [Header("View Limits")]
    [SerializeField, Range(-89f, 0f)] private float minimumPitch = -80f;
    [SerializeField, Range(0f, 89f)] private float maximumPitch = 80f;
    [SerializeField, Min(0.01f)] private float minimumDistance = 1f;
    [SerializeField, Min(0.01f)] private float maximumDistance = 30f;
    [SerializeField, Min(0.01f)] private float minimumOrthographicSize = 0.5f;
    [SerializeField, Min(0.01f)] private float maximumOrthographicSize = 12f;
    [SerializeField, Min(0f)] private float domainClearance = 0.25f;

    [Header("Domain Bounds Fallback")]
    [SerializeField] private Vector3 fallbackDomainCenter = new Vector3(0f, 1f, 0f);
    [SerializeField] private Vector3 fallbackDomainSize = new Vector3(5f, 2f, 8f);

    private readonly List<RaycastResult> raycastResults = new List<RaycastResult>(16);
    private readonly Vector3[] inputAreaWorldCorners = new Vector3[4];

    private Camera controlledCamera;
    private Canvas inputCanvas;
    private EventSystem pointerEventSystem;
    private PointerEventData pointerEventData;
    private Bounds domainBounds;
    private DragMode dragMode;
    private Vector3 pivot;
    private float yaw;
    private float pitch;
    private float distance;

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Vector3 initialPivot;
    private float initialDistance;
    private float initialOrthographicSize;

    private void Awake()
    {
        controlledCamera = GetComponent<Camera>();
        inputCanvas = inputArea != null ? inputArea.GetComponentInParent<Canvas>() : null;

        ResolveDomainBounds();
        InitializeViewFromDomainCenter();
        SaveInitialView();
    }

    private void OnDisable()
    {
        dragMode = DragMode.None;
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        Vector2 pointerPosition = mouse.position.ReadValue();
        bool pointerCanControlCamera = CanUsePointer(pointerPosition);

        if (mouse.leftButton.wasPressedThisFrame && pointerCanControlCamera)
        {
            dragMode = DragMode.Orbit;
        }
        else if (mouse.rightButton.wasPressedThisFrame && pointerCanControlCamera)
        {
            dragMode = DragMode.Pan;
        }

        if (mouse.leftButton.wasReleasedThisFrame || mouse.rightButton.wasReleasedThisFrame)
        {
            dragMode = DragMode.None;
        }

        Vector2 pointerDelta = mouse.delta.ReadValue();
        if (pointerCanControlCamera && pointerDelta.sqrMagnitude > 0f)
        {
            if (dragMode == DragMode.Orbit && mouse.leftButton.isPressed && !mouse.rightButton.isPressed)
            {
                Orbit(pointerDelta);
            }
            else if (dragMode == DragMode.Pan && mouse.rightButton.isPressed && !mouse.leftButton.isPressed)
            {
                Pan(pointerDelta);
            }
        }

        float scroll = mouse.scroll.ReadValue().y;
        if (pointerCanControlCamera && !Mathf.Approximately(scroll, 0f))
        {
            Zoom(scroll);
        }

        Keyboard keyboard = Keyboard.current;
        if (enableRKeyReset && keyboard != null && keyboard.rKey.wasPressedThisFrame && pointerCanControlCamera)
        {
            ResetView();
        }
    }

    public void ResetView()
    {
        pivot = initialPivot;
        distance = initialDistance;
        controlledCamera.orthographicSize = initialOrthographicSize;
        transform.SetPositionAndRotation(initialPosition, initialRotation);
        UpdateAnglesFromRotation(initialRotation);
        dragMode = DragMode.None;
    }

    private void ResolveDomainBounds()
    {
        bool foundRenderer = false;
        Bounds resolvedBounds = default;

        if (domainRoot != null)
        {
            Renderer[] renderers = domainRoot.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer domainRenderer in renderers)
            {
                if (domainRenderer == null)
                {
                    continue;
                }

                if (!foundRenderer)
                {
                    resolvedBounds = domainRenderer.bounds;
                    foundRenderer = true;
                }
                else
                {
                    resolvedBounds.Encapsulate(domainRenderer.bounds);
                }
            }
        }

        Vector3 safeFallbackSize = new Vector3(
            Mathf.Max(0.01f, Mathf.Abs(fallbackDomainSize.x)),
            Mathf.Max(0.01f, Mathf.Abs(fallbackDomainSize.y)),
            Mathf.Max(0.01f, Mathf.Abs(fallbackDomainSize.z)));

        domainBounds = foundRenderer
            ? resolvedBounds
            : new Bounds(fallbackDomainCenter, safeFallbackSize);
    }

    private void InitializeViewFromDomainCenter()
    {
        pivot = domainBounds.center;
        Vector3 viewDirection = pivot - transform.position;

        if (viewDirection.sqrMagnitude > 0.000001f)
        {
            transform.rotation = Quaternion.LookRotation(viewDirection.normalized, Vector3.up);
            distance = viewDirection.magnitude;
        }
        else
        {
            distance = GetRequiredDistance(transform.rotation);
            transform.position = pivot - transform.forward * distance;
        }

        UpdateAnglesFromRotation(transform.rotation);
        distance = ClampDistance(distance, transform.rotation);
        transform.position = pivot - transform.forward * distance;
        controlledCamera.orthographicSize = Mathf.Clamp(
            controlledCamera.orthographicSize,
            minimumOrthographicSize,
            maximumOrthographicSize);
    }

    private void SaveInitialView()
    {
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        initialPivot = pivot;
        initialDistance = distance;
        initialOrthographicSize = controlledCamera.orthographicSize;
    }

    private void Orbit(Vector2 pointerDelta)
    {
        float viewportHeight = GetInputAreaHeight();
        float degreesPerPixel = orbitDegreesPerViewport / viewportHeight;

        yaw += pointerDelta.x * degreesPerPixel;
        pitch = Mathf.Clamp(pitch - pointerDelta.y * degreesPerPixel, minimumPitch, maximumPitch);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        distance = ClampDistance(distance, rotation);
        transform.SetPositionAndRotation(pivot - rotation * Vector3.forward * distance, rotation);
    }

    private void Pan(Vector2 pointerDelta)
    {
        float viewportHeight = GetInputAreaHeight();
        float verticalSpan = controlledCamera.orthographic
            ? controlledCamera.orthographicSize * 2f
            : 2f * distance * Mathf.Tan(controlledCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float worldUnitsPerPixel = verticalSpan / viewportHeight;

        Vector3 movement = (-transform.right * pointerDelta.x - transform.up * pointerDelta.y)
            * worldUnitsPerPixel
            * panSpeedMultiplier;
        pivot = ClampPointToDomain(pivot + movement);

        distance = ClampDistance(distance, transform.rotation);
        transform.position = pivot - transform.forward * distance;
    }

    private void Zoom(float scrollDelta)
    {
        // The Input System reports one Windows wheel notch as approximately 120 units.
        float wheelNotches = scrollDelta / 120f;
        float zoomFactor = Mathf.Exp(-wheelNotches * zoomSensitivity);

        if (controlledCamera.orthographic)
        {
            controlledCamera.orthographicSize = Mathf.Clamp(
                controlledCamera.orthographicSize * zoomFactor,
                minimumOrthographicSize,
                maximumOrthographicSize);
            return;
        }

        distance = ClampDistance(distance * zoomFactor, transform.rotation);
        transform.position = pivot - transform.forward * distance;
    }

    private bool CanUsePointer(Vector2 screenPosition)
    {
        if (inputArea == null || !inputArea.gameObject.activeInHierarchy)
        {
            return false;
        }

        Camera canvasCamera = inputCanvas != null && inputCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? inputCanvas.worldCamera
            : null;
        if (!RectTransformUtility.RectangleContainsScreenPoint(inputArea, screenPosition, canvasCamera))
        {
            return false;
        }

        EventSystem currentEventSystem = EventSystem.current;
        if (currentEventSystem == null)
        {
            return true;
        }

        if (pointerEventSystem != currentEventSystem || pointerEventData == null)
        {
            pointerEventSystem = currentEventSystem;
            pointerEventData = new PointerEventData(currentEventSystem);
        }

        pointerEventData.Reset();
        pointerEventData.position = screenPosition;
        raycastResults.Clear();
        currentEventSystem.RaycastAll(pointerEventData, raycastResults);

        if (raycastResults.Count == 0)
        {
            return true;
        }

        Transform topHit = raycastResults[0].gameObject.transform;
        return topHit == inputArea || topHit.IsChildOf(inputArea);
    }

    private Vector3 ClampPointToDomain(Vector3 point)
    {
        Vector3 minimum = domainBounds.min;
        Vector3 maximum = domainBounds.max;
        return new Vector3(
            Mathf.Clamp(point.x, minimum.x, maximum.x),
            Mathf.Clamp(point.y, minimum.y, maximum.y),
            Mathf.Clamp(point.z, minimum.z, maximum.z));
    }

    private float ClampDistance(float requestedDistance, Quaternion rotation)
    {
        float requiredDistance = GetRequiredDistance(rotation);
        float safeMinimum = Mathf.Max(minimumDistance, requiredDistance);
        float safeMaximum = Mathf.Max(safeMinimum, maximumDistance);
        return Mathf.Clamp(requestedDistance, safeMinimum, safeMaximum);
    }

    private float GetRequiredDistance(Quaternion rotation)
    {
        Vector3 directionFromPivotToCamera = -(rotation * Vector3.forward);
        float exitDistance = GetRayExitDistance(pivot, directionFromPivotToCamera, domainBounds);
        return exitDistance + Mathf.Max(domainClearance, controlledCamera.nearClipPlane);
    }

    private static float GetRayExitDistance(Vector3 origin, Vector3 direction, Bounds bounds)
    {
        Vector3 minimum = bounds.min;
        Vector3 maximum = bounds.max;
        float exitDistance = float.PositiveInfinity;

        UpdateExitDistance(origin.x, direction.x, minimum.x, maximum.x, ref exitDistance);
        UpdateExitDistance(origin.y, direction.y, minimum.y, maximum.y, ref exitDistance);
        UpdateExitDistance(origin.z, direction.z, minimum.z, maximum.z, ref exitDistance);

        return float.IsInfinity(exitDistance) ? 0f : Mathf.Max(0f, exitDistance);
    }

    private static void UpdateExitDistance(
        float origin,
        float direction,
        float minimum,
        float maximum,
        ref float exitDistance)
    {
        if (Mathf.Abs(direction) < 0.000001f)
        {
            return;
        }

        float boundary = direction > 0f ? maximum : minimum;
        float distanceToBoundary = (boundary - origin) / direction;
        if (distanceToBoundary >= 0f)
        {
            exitDistance = Mathf.Min(exitDistance, distanceToBoundary);
        }
    }

    private void UpdateAnglesFromRotation(Quaternion rotation)
    {
        Vector3 forward = rotation * Vector3.forward;
        yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        pitch = -Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        pitch = Mathf.Clamp(pitch, minimumPitch, maximumPitch);
    }

    private float GetInputAreaHeight()
    {
        if (inputArea == null)
        {
            return Mathf.Max(1f, Screen.height);
        }

        Camera canvasCamera = inputCanvas != null && inputCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? inputCanvas.worldCamera
            : null;
        inputArea.GetWorldCorners(inputAreaWorldCorners);
        Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(canvasCamera, inputAreaWorldCorners[0]);
        Vector2 topLeft = RectTransformUtility.WorldToScreenPoint(canvasCamera, inputAreaWorldCorners[1]);
        return Mathf.Max(1f, Vector2.Distance(bottomLeft, topLeft));
    }

    private void OnValidate()
    {
        maximumPitch = Mathf.Max(0f, maximumPitch);
        minimumPitch = Mathf.Min(0f, minimumPitch);
        maximumDistance = Mathf.Max(minimumDistance, maximumDistance);
        maximumOrthographicSize = Mathf.Max(minimumOrthographicSize, maximumOrthographicSize);
        fallbackDomainSize = new Vector3(
            Mathf.Max(0.01f, Mathf.Abs(fallbackDomainSize.x)),
            Mathf.Max(0.01f, Mathf.Abs(fallbackDomainSize.y)),
            Mathf.Max(0.01f, Mathf.Abs(fallbackDomainSize.z)));
    }
}
