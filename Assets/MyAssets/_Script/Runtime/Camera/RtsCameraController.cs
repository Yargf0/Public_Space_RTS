using UnityEngine;

public class RtsCamera2D : MonoBehaviour
{
    [Header("Move (XY)")]
    public float moveSpeed = 20f;
    public float dragSpeed = 1f;
    public bool edgeScroll = true;
    public float edgeSize = 15f;
    public int dragMouseButton = 1;

    [Header("Zoom")]
    public float zoomSpeed = 200f;
    public float minOrthoSize = 5f;
    public float maxOrthoSize = 35f;

    [Header("Bounds (world XY)")]
    public bool useBounds = true;
    public Vector2 minBounds = new Vector2(-100, -100);
    public Vector2 maxBounds = new Vector2(100, 100);

    public Camera cam;

    private Vector3 lastMousePosition;

    void Awake()
    {
        if (cam == null)
        {
            cam = GetComponent<Camera>();
        }

        if (cam == null)
        {
            cam = GetComponentInChildren<Camera>();
        }

        if (cam == null)
        {
            cam = Camera.main;
        }
    }

    void Update()
    {
        if (cam == null) { return; }

        HandleKeyboardAndEdgeMove();
        //HandleDragMove();
        HandleZoom();
        ClampToBounds();
    }

    void HandleKeyboardAndEdgeMove()
    {
        Vector2 move = InputProvider.Instance.MoveInput;

        if (edgeScroll)
        {
            Vector2 pointer = InputProvider.Instance.PointerPosition;

            if (pointer.x <= edgeSize) { move.x -= 1f; }
            if (pointer.x >= Screen.width - edgeSize) { move.x += 1f; }
            if (pointer.y <= edgeSize) { move.y -= 1f; }
            if (pointer.y >= Screen.height - edgeSize) { move.y += 1f; }
        }

        if (move.sqrMagnitude > 1f) { move.Normalize(); }

        float zoomFactor = Mathf.InverseLerp(minOrthoSize, maxOrthoSize, cam.orthographicSize);
        float scaledSpeed = Mathf.Lerp(moveSpeed * 0.6f, moveSpeed * 1.6f, zoomFactor);

        Vector3 pos = cam.transform.position;
        pos.x += move.x * scaledSpeed * Time.deltaTime;
        pos.y += move.y * scaledSpeed * Time.deltaTime;
        cam.transform.position = pos;
    }

    void HandleDragMove()
    {
        if (Input.GetMouseButtonDown(dragMouseButton))
        {
            lastMousePosition = Input.mousePosition;
        }

        if (Input.GetMouseButton(dragMouseButton))
        {
            Vector3 mouseDelta = Input.mousePosition - lastMousePosition;
            lastMousePosition = Input.mousePosition;

            float sizeFactor = cam.orthographicSize / 10f;

            Vector3 pos = cam.transform.position;
            pos.x += -mouseDelta.x * dragSpeed * sizeFactor * Time.deltaTime;
            pos.y += -mouseDelta.y * dragSpeed * sizeFactor * Time.deltaTime;
            cam.transform.position = pos;
        }
    }

    void HandleZoom()
    {
        Vector2 scrollVector = InputProvider.Instance.ScrollDelta;

        if (scrollVector == Vector2.zero) { return; }

        float scrollValue = scrollVector.y;
        float zoomChange = scrollValue * zoomSpeed * Time.deltaTime;
        float nextSize = cam.orthographicSize - zoomChange;

        cam.orthographicSize = Mathf.Clamp(nextSize, minOrthoSize, maxOrthoSize);
    }

    void ClampToBounds()
    {
        if (!useBounds) { return; }

        Vector3 pos = cam.transform.position;
        pos.x = Mathf.Clamp(pos.x, minBounds.x, maxBounds.x);
        pos.y = Mathf.Clamp(pos.y, minBounds.y, maxBounds.y);
        cam.transform.position = pos;
    }
}