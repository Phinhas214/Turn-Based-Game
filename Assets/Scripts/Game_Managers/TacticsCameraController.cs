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

    [Header("Auto-Focus (Room Transitions)")]
    [SerializeField] private Vector3 followOffset = new Vector3(0f, 20f, -2f);
    [SerializeField] private float snapSmoothness = 5f;

    private Vector3 basePosition;
    private Vector3 lastMousePosition;
    private float targetOrthoSize;

    private bool isSnappingToPlayer = false;

    Bounds roomBounds;
    bool hasBounds = false;

    public void SetRoomBounds(Bounds bounds)
    {
        roomBounds = bounds;
        hasBounds = true;

        Debug.Log(
            "🎥 Camera bounds set → Center: " +
            bounds.center +
            " Size: " +
            bounds.size
        );
    }

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

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

        ClampToRoom();  // ← FIX #1

        transform.position = basePosition; // ← FIX #2
    }

    public void FocusOnPlayer()
    {
        isSnappingToPlayer = true;
    }

    private void SnapToPlayerLogic()
    {
        Transform player = PlayerTarget.Instance?.transform;

        if (player == null) return;

        Vector3 targetPos = player.position + followOffset;

        basePosition = Vector3.Lerp(
            basePosition,
            targetPos,
            Time.deltaTime * snapSmoothness
        );

        if (Vector3.Distance(basePosition, targetPos) < 0.01f)
        {
            basePosition = targetPos;
            isSnappingToPlayer = false;
        }
    }

    private bool HasManualInput()
    {
        return Input.GetAxisRaw("Horizontal") != 0 ||
               Input.GetAxisRaw("Vertical") != 0 ||
               Input.GetMouseButton(1);
    }

    void HandleKeyboardPan()
    {
        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");

        Vector3 move = new Vector3(x, 0f, z).normalized;

        basePosition += move * panSpeed * Time.deltaTime;
    }

    void HandleMouseDragPan()
    {
        if (Input.GetMouseButtonDown(1))
            lastMousePosition = Input.mousePosition;

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

        if (Mathf.Abs(scroll) < 0.01f) return;

        Vector3 mouseWorldBefore = GetMouseWorldPosition();

        targetOrthoSize = Mathf.Clamp(
            targetOrthoSize - scroll * zoomSpeed,
            orthoMin,
            orthoMax
        );

        cam.orthographicSize = Mathf.Lerp(
            cam.orthographicSize,
            targetOrthoSize,
            Time.deltaTime * 10f
        );

        Vector3 mouseWorldAfter = GetMouseWorldPosition();

        Vector3 difference = mouseWorldBefore - mouseWorldAfter;

        basePosition += difference;
    }

    Vector3 GetMouseWorldPosition()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        Plane plane = new Plane(Vector3.up, Vector3.zero);

        if (plane.Raycast(ray, out float distance))
        {
            return ray.GetPoint(distance);
        }

        return Vector3.zero;
    }

    void ClampToRoom()
    {

        if (!hasBounds || cam == null)
        {
            Debug.Log("⚠ Clamp skipped: no bounds yet");
            return;
        }

        Debug.Log("🔒 Camera clamping active");

        if (!hasBounds || cam == null) return;

        Vector3 pos = basePosition;

        float camHeight = cam.orthographicSize;
        float camWidth = camHeight * cam.aspect;

        float minX = roomBounds.min.x + camWidth;
        float maxX = roomBounds.max.x - camWidth;

        float minZ = roomBounds.min.z + camHeight;
        float maxZ = roomBounds.max.z - camHeight;

        pos.x = Mathf.Clamp(pos.x, minX, maxX);
        pos.z = Mathf.Clamp(pos.z, minZ, maxZ);

        basePosition = pos;
    }
}