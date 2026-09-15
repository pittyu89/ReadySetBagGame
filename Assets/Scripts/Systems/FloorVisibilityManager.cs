using System.Collections.Generic;
using UnityEngine;

public class FloorVisibilityManager : MonoBehaviour
{
    [SerializeField] private GameObject stairs;
    [SerializeField] private GameObject secondFloor;

    [Header("Reveal Animation")]
    [Tooltip("How far above its resting spot each piece starts when the 2nd floor appears. 0 or less = a fraction of the stairs' height.")]
    [SerializeField] private float dropHeight = 0f;
    [Tooltip("Seconds each piece takes to settle into place.")]
    [SerializeField] private float pieceDuration = 0.45f;
    [Tooltip("Extra delay per unit of distance from the player, so pieces ripple outward.")]
    [SerializeField] private float delayPerUnit = 0.025f;
    [Tooltip("Longest delay any piece waits before starting, so far-away pieces don't lag behind.")]
    [SerializeField] private float maxDelay = 0.35f;
    [Tooltip("Seconds for the 2nd floor to lift away when the player goes back down.")]
    [SerializeField] private float hideDuration = 0.25f;
    [Tooltip("Height band around the trigger point that stops the floor flickering when the player stands right on it.")]
    [SerializeField] private float hysteresis = 0.15f;

    // Name of the floor slab inside the 2nd floor group; it lands first so the rest has something to land on.
    private const string FLOOR_SLAB_NAME = "2nd floor";

    private class Piece
    {
        public Transform transform;
        public Vector3 restLocalPosition;
        public Vector3 startLocalPosition;
        public float delay;
    }

    private readonly List<Piece> pieces = new List<Piece>();
    private float stairsMidpointHeight;
    private Transform playerTransform;
    private bool isSecondFloorVisible;
    private bool isAnimating;
    private float animationTime;
    private float animationLength;

    private void Start()
    {
        // Calculate stairs midpoint height
        if (stairs != null)
        {
            Bounds bounds = GetBounds(stairs);
            stairsMidpointHeight = bounds.center.y + bounds.extents.y / 2f;

            if (dropHeight <= 0f)
                dropHeight = Mathf.Max(bounds.size.y * 0.5f, 1f);
        }
        else if (dropHeight <= 0f)
        {
            dropHeight = 2f;
        }

        // Find the player
        playerTransform = FindObjectOfType<PlayerMovement>()?.transform;

        if (playerTransform == null)
        {
            // Fallback: try to find by tag
            GameObject playerGO = GameObject.FindWithTag("Player");
            if (playerGO != null)
                playerTransform = playerGO.transform;
        }

        // Initially hide second floor
        if (secondFloor != null)
        {
            CachePieces();
            secondFloor.SetActive(false);
            isSecondFloorVisible = false;
        }
    }

    private void Update()
    {
        // Only check if we have a player reference
        if (playerTransform == null || secondFloor == null)
            return;

        float playerHeight = playerTransform.position.y;
        float threshold = isSecondFloorVisible
            ? stairsMidpointHeight - hysteresis
            : stairsMidpointHeight + hysteresis;
        bool shouldBeVisible = playerHeight > threshold;

        // Only update if visibility state changed
        if (shouldBeVisible != isSecondFloorVisible)
        {
            isSecondFloorVisible = shouldBeVisible;
            if (shouldBeVisible)
                BeginShow();
            else
                BeginHide();
        }

        if (isAnimating)
            Animate();
    }

    private void CachePieces()
    {
        pieces.Clear();
        foreach (Transform child in secondFloor.transform)
        {
            pieces.Add(new Piece
            {
                transform = child,
                restLocalPosition = child.localPosition
            });
        }
    }

    private void BeginShow()
    {
        secondFloor.SetActive(true);

        Vector3 localDrop = secondFloor.transform.InverseTransformVector(Vector3.up * dropHeight);
        Vector3 playerFlat = new Vector3(playerTransform.position.x, 0f, playerTransform.position.z);

        float longestDelay = 0f;
        foreach (Piece piece in pieces)
        {
            if (piece.transform == null)
                continue;

            // If the floor was mid-lift, start from where the piece is now rather than snapping.
            Vector3 raised = piece.restLocalPosition + localDrop;
            piece.startLocalPosition = isAnimating ? piece.transform.localPosition : raised;
            piece.transform.localPosition = piece.startLocalPosition;

            if (piece.transform.name == FLOOR_SLAB_NAME)
            {
                piece.delay = 0f;
            }
            else
            {
                Vector3 worldRest = secondFloor.transform.TransformPoint(piece.restLocalPosition);
                float distance = Vector3.Distance(playerFlat, new Vector3(worldRest.x, 0f, worldRest.z));
                // Everything else waits a beat so the floor lands first, then ripples out from the player.
                piece.delay = Mathf.Min(0.08f + distance * delayPerUnit, maxDelay);
            }

            longestDelay = Mathf.Max(longestDelay, piece.delay);
        }

        animationTime = 0f;
        animationLength = longestDelay + pieceDuration;
        isAnimating = true;
    }

    private void BeginHide()
    {
        foreach (Piece piece in pieces)
        {
            if (piece.transform == null)
                continue;
            piece.startLocalPosition = piece.transform.localPosition;
            piece.delay = 0f;
        }

        animationTime = 0f;
        animationLength = hideDuration;
        isAnimating = true;
    }

    private void Animate()
    {
        animationTime += Time.deltaTime;
        Vector3 localDrop = secondFloor.transform.InverseTransformVector(Vector3.up * dropHeight);

        foreach (Piece piece in pieces)
        {
            if (piece.transform == null)
                continue;

            if (isSecondFloorVisible)
            {
                float t = Mathf.Clamp01((animationTime - piece.delay) / pieceDuration);
                piece.transform.localPosition = Vector3.LerpUnclamped(
                    piece.startLocalPosition, piece.restLocalPosition, EaseOutBack(t));
            }
            else
            {
                float t = Mathf.Clamp01(animationTime / hideDuration);
                piece.transform.localPosition = Vector3.LerpUnclamped(
                    piece.startLocalPosition, piece.restLocalPosition + localDrop, EaseInCubic(t));
            }
        }

        if (animationTime < animationLength)
            return;

        isAnimating = false;
        // Always leave pieces exactly at rest, so gameplay positions never drift.
        foreach (Piece piece in pieces)
        {
            if (piece.transform != null)
                piece.transform.localPosition = piece.restLocalPosition;
        }

        if (!isSecondFloorVisible)
            secondFloor.SetActive(false);
    }

    // Slight overshoot then settle, which reads as the piece "landing".
    private static float EaseOutBack(float t)
    {
        const float overshoot = 1.2f;
        float p = t - 1f;
        return 1f + (overshoot + 1f) * p * p * p + overshoot * p * p;
    }

    private static float EaseInCubic(float t)
    {
        return t * t * t;
    }

    private Bounds GetBounds(GameObject obj)
    {
        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer != null)
            return renderer.bounds;

        // If no renderer, try to get bounds from children
        Bounds bounds = new Bounds(obj.transform.position, Vector3.zero);
        foreach (Renderer childRenderer in obj.GetComponentsInChildren<Renderer>())
        {
            bounds.Encapsulate(childRenderer.bounds);
        }

        return bounds;
    }
}
