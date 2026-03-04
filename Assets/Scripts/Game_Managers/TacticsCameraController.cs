using UnityEngine;

public class FreeTacticsCameraController : MonoBehaviour
{
    public static FreeTacticsCameraController Instance { get; private set; }

    [Header("References")]
    public Camera cam;

    [Header("Pan")]
    public float panSpeed = 12f;
    public float dragPanSpeed = 0.02f;

    [Header("Zoom")]
    public float zoomSpeed = 5f;
    public float orthoMin = 4f;
    public float orthoMax = 18f;

    [Header("Shake")]
    [SerializeField] private float shakeAmplitude = 0.25f;
    [SerializeField] private float shakeFrequency = 20f;
    [SerializeField] private float shakeDuration = 0.15f;

    [Header("Auto-Focus (Room Transitions)")]
    [SerializeField] private Vector3 followOffset = new Vector3(0f, 20f, -2f);
    [SerializeField] private float snapSmoothness = 5f;

    private Vector3 basePosition;
    private Vector3 shakeOffset;
    private float shakeTimeRemaining;
    private Vector3 lastMousePosition;
    private float targetOrthoSize;

    // State for snapping
    private bool isSnappingToPlayer = false;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

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
        // If the player tries to move the camera manually, cancel the auto-snap
        if (HasManualInput())
        {
            isSnappingToPlayer = false;
        }

        if (isSnappingToPlayer)
        {
            SnapToPlayerLogic();
        }
        else
        {
            HandleKeyboardPan();
            HandleMouseDragPan();
        }

        HandleZoom();
    }

    private bool HasManualInput()
    {
        return Input.GetAxisRaw("Horizontal") != 0 || 
               Input.GetAxisRaw("Vertical") != 0 || 
               Input.GetMouseButton(1);
    }

    private void SnapToPlayerLogic()
    {
        Transform player = PlayerTarget.Instance?.transform; // Ensure PlayerTarget.Instance exists!
        if (player != null)
        {
            Vector3 targetPos = player.position + followOffset;
            basePosition = Vector3.Lerp(basePosition, targetPos, Time.deltaTime * snapSmoothness);

            // Stop snapping once we are pixel-perfect (or very close)
            if (Vector3.Distance(basePosition, targetPos) < 0.01f)
            {
                basePosition = targetPos;
                isSnappingToPlayer = false;
            }
        }
    }

    void LateUpdate()
    {
        ApplyShake();
    }

    // --- Public API ---

    public void FocusOnPlayer()
    {
        isSnappingToPlayer = true;
    }

    public void Shake(float intensityMultiplier = 1f)
    {
        shakeTimeRemaining = shakeDuration;
    }

    // --- Movement Logic ---

    void HandleKeyboardPan()
    {
        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");

        Vector3 move = new Vector3(x, 0f, z).normalized;
        basePosition += move * panSpeed * Time.deltaTime;
    }

    void HandleMouseDragPan()
    {
        if (Input.GetMouseButtonDown(1)) lastMousePosition = Input.mousePosition;

        if (Input.GetMouseButton(1))
        {
            Vector3 delta = Input.mousePosition - lastMousePosition;
            lastMousePosition = Input.mousePosition;
            Vector3 drag = new Vector3(-delta.x, 0f, -delta.y);
            basePosition += drag * dragPanSpeed;
        }
    }

    void HandleZoom()
    {
        if (!cam) return;
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            targetOrthoSize = Mathf.Clamp(targetOrthoSize - scroll * zoomSpeed, orthoMin, orthoMax);
        }

        cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetOrthoSize, Time.deltaTime * 8f);
    }

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
        else { shakeOffset = Vector3.zero; }

        transform.position = basePosition + shakeOffset;
    }
}