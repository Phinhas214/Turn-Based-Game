using UnityEngine;

public class FreeTacticsCameraController : MonoBehaviour
{
    [Header("References")]
    public Camera cam;

    // ─────────────────────────────────────────
    // Pan
    // ─────────────────────────────────────────
    [Header("Pan")]
    public float panSpeed = 12f;
    public float dragPanSpeed = 0.02f;

    // ─────────────────────────────────────────
    // Zoom
    // ─────────────────────────────────────────
    [Header("Zoom")]
    public float zoomSpeed = 5f;
    public float orthoMin = 4f;
    public float orthoMax = 18f;

    // ─────────────────────────────────────────
    // Shake
    // ─────────────────────────────────────────
    [Header("Shake")]
    [SerializeField] private float shakeAmplitude = 0.25f;
    [SerializeField] private float shakeFrequency = 20f;
    [SerializeField] private float shakeDuration = 0.15f;

    // ─────────────────────────────────────────
    // Internal state
    // ─────────────────────────────────────────
    private Vector3 basePosition;
    private Vector3 shakeOffset;

    private float shakeTimeRemaining;

    private Vector3 lastMousePosition;

    private float targetOrthoSize;

    void Awake()
    {
        if (!cam) cam = Camera.main;

        if (cam)
        {
            cam.orthographic = true;
            targetOrthoSize = cam.orthographicSize;
        }

        basePosition = transform.position;
    }

    void Update()
    {
        HandleKeyboardPan();
        HandleMouseDragPan();
        HandleZoom();
    }

    void LateUpdate()
    {
        ApplyShake();
    }

    // ─────────────────────────────────────────
    // Pan
    // ─────────────────────────────────────────

    void HandleKeyboardPan()
    {
        float x = 0f;
        float z = 0f;

        if (Input.GetKey(KeyCode.W)) z += 1f;
        if (Input.GetKey(KeyCode.S)) z -= 1f;
        if (Input.GetKey(KeyCode.D)) x += 1f;
        if (Input.GetKey(KeyCode.A)) x -= 1f;

        Vector3 move = new Vector3(x, 0f, z).normalized;
        basePosition += move * panSpeed * Time.deltaTime;
    }

    void HandleMouseDragPan()
    {
        if (Input.GetMouseButtonDown(1))
        {
            lastMousePosition = Input.mousePosition;
        }

        if (Input.GetMouseButton(1))
        {
            Vector3 delta = Input.mousePosition - lastMousePosition;
            lastMousePosition = Input.mousePosition;

            // Invert so dragging feels natural
            Vector3 drag = new Vector3(-delta.x, 0f, -delta.y);
            basePosition += drag * dragPanSpeed;
        }
    }

    // ─────────────────────────────────────────
    // Zoom
    // ─────────────────────────────────────────

    void HandleZoom()
    {
        if (!cam) return;

        float scroll = Input.mouseScrollDelta.y;

        if (Mathf.Abs(scroll) > 0.01f)
        {
            targetOrthoSize = Mathf.Clamp(
                targetOrthoSize - scroll * zoomSpeed,
                orthoMin,
                orthoMax
            );
        }

        cam.orthographicSize = Mathf.Lerp(
            cam.orthographicSize,
            targetOrthoSize,
            Time.deltaTime * 8f
        );
    }

    // ─────────────────────────────────────────
    // Shake
    // ─────────────────────────────────────────

    void ApplyShake()
    {
        if (shakeTimeRemaining > 0f)
        {
            shakeTimeRemaining -= Time.deltaTime;

            float t = shakeTimeRemaining / shakeDuration;
            float strength = shakeAmplitude * t;

            float noiseX = (Mathf.PerlinNoise(Time.time * shakeFrequency, 0f) - 0.5f) * 2f;
            float noiseZ = (Mathf.PerlinNoise(0f, Time.time * shakeFrequency) - 0.5f) * 2f;

            shakeOffset = new Vector3(noiseX, 0f, noiseZ) * strength;
        }
        else
        {
            shakeOffset = Vector3.zero;
        }

        transform.position = basePosition + shakeOffset;
    }

    // ─────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────

    public void Shake(float intensityMultiplier = 1f)
    {
        shakeTimeRemaining = shakeDuration;
    }
}