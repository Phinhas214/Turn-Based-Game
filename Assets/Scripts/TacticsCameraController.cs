using UnityEngine;

public class FreeTacticsCameraController : MonoBehaviour
{
    [Header("References")]
    public Camera cam;

    [Header("Pan")]
    public float panSpeed = 12f;

    [Header("Zoom")]
    public float zoomSpeed = 5f;
    public float orthoMin = 4f;
    public float orthoMax = 18f;

    float targetOrthoSize;

    void Awake()
    {
        if (!cam) cam = Camera.main;

        if (cam)
        {
            cam.orthographic = true;
            targetOrthoSize = cam.orthographicSize;
        }
    }


    void Update()
    {
        HandlePan();
        HandleZoom();
    }

    void HandlePan()
    {
        float x = 0f;
        float z = 0f;

        if (Input.GetKey(KeyCode.W)) z += 1f;
        if (Input.GetKey(KeyCode.S)) z -= 1f;
        if (Input.GetKey(KeyCode.D)) x += 1f;
        if (Input.GetKey(KeyCode.A)) x -= 1f;

        Vector3 move = new Vector3(x, 0f, z).normalized;

        transform.position += move * panSpeed * Time.deltaTime;
    }

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

        // Smoothly move toward target zoom
        cam.orthographicSize = Mathf.Lerp(
            cam.orthographicSize,
            targetOrthoSize,
            Time.deltaTime * 8f
        );
    }

}
