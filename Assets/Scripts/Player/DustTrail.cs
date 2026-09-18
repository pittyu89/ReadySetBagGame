using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Leaves a short trail of little cloud puffs behind the character while they walk: a puff is
/// dropped every <see cref="spawnInterval"/> seconds and sits on the floor where it was left,
/// shrinking and fading until the next <see cref="maxPuffs"/> drops have replaced it. So there
/// is always a solid, full-sized puff at the heels, then smaller fainter ones behind it, and
/// never more than <see cref="maxPuffs"/> at once. Puffs face the camera like the character
/// does and go back to a small pool when gone.
/// </summary>
public class DustTrail : MonoBehaviour
{
    [Tooltip("The cloud every puff is drawn with.")]
    [SerializeField] private Sprite puffSprite;
    [Tooltip("Seconds between puffs while walking. Higher is a slower, calmer trail.")]
    [SerializeField] private float spawnInterval = 0.15f;
    [Tooltip("Most puffs on screen at once. Each one shrinks away over this many drops.")]
    [SerializeField] private int maxPuffs = 3;
    [Tooltip("Slower than this (units/second) counts as standing still.")]
    [SerializeField] private float minSpeed = 0.5f;
    [Tooltip("Scale of a puff when it is dropped.")]
    [SerializeField] private float puffScale = 0.7f;
    [Tooltip("Puffs appear this far behind the feet, against the direction of travel.")]
    [SerializeField] private float behindOffset = 0.2f;
    [Tooltip("Tiny lift so puffs don't clip into the floor.")]
    [SerializeField] private float floorLift = 0.04f;

    private class Puff
    {
        public Transform transform;
        public SpriteRenderer renderer;
        public float age;
    }

    private readonly List<Puff> active = new List<Puff>();
    private readonly Stack<Puff> pool = new Stack<Puff>();
    private CharacterController controller;
    private Camera mainCamera;
    private Vector3 lastPosition;
    private float sinceLastPuff;
    private Transform puffParent;

    // Each puff lasts exactly as long as it takes the next few to be dropped
    private float Lifetime => spawnInterval * Mathf.Max(1, maxPuffs);

    void Start()
    {
        controller = GetComponent<CharacterController>();
        lastPosition = transform.position;

        // Puffs stay where they were dropped, so they live outside the moving character
        puffParent = new GameObject(name + " Dust").transform;
    }

    void OnDestroy()
    {
        if (puffParent != null)
            Destroy(puffParent.gameObject);
    }

    void Update()
    {
        Vector3 position = transform.position;
        Vector3 step = position - lastPosition;
        step.y = 0f;
        lastPosition = position;

        float speed = Time.deltaTime > 0f ? step.magnitude / Time.deltaTime : 0f;
        bool grounded = controller == null || controller.isGrounded;

        if (puffSprite != null && grounded && speed >= minSpeed)
        {
            sinceLastPuff += Time.deltaTime;
            if (sinceLastPuff >= spawnInterval)
            {
                sinceLastPuff = 0f;
                Spawn(FeetPosition() - step.normalized * behindOffset);
            }
        }
        else
        {
            // The first step after stopping leaves a puff straight away
            sinceLastPuff = spawnInterval;
        }

        Animate();
    }

    private Vector3 FeetPosition()
    {
        float bottom = transform.position.y;
        if (controller != null)
            bottom += (controller.center.y - controller.height * 0.5f) * transform.lossyScale.y - controller.skinWidth;

        return new Vector3(transform.position.x, bottom + floorLift, transform.position.z);
    }

    private void Spawn(Vector3 position)
    {
        // A long frame can drop the next puff a moment before the oldest has shrunk away;
        // clear it out so there are never more than maxPuffs on screen
        while (active.Count >= Mathf.Max(1, maxPuffs))
            Recycle(0);

        Puff puff = pool.Count > 0 ? pool.Pop() : CreatePuff();
        puff.age = 0f;
        puff.transform.position = position;
        puff.transform.localScale = Vector3.one * puffScale;
        puff.renderer.sprite = puffSprite;
        puff.renderer.color = Color.white;
        // Mirroring every other puff stops the trail looking stamped
        puff.renderer.flipX = !puff.renderer.flipX;
        puff.transform.gameObject.SetActive(true);
        active.Add(puff);
    }

    private void Recycle(int index)
    {
        Puff puff = active[index];
        puff.transform.gameObject.SetActive(false);
        active.RemoveAt(index);
        pool.Push(puff);
    }

    private Puff CreatePuff()
    {
        GameObject go = new GameObject("Puff");
        go.transform.SetParent(puffParent, false);
        return new Puff { transform = go.transform, renderer = go.AddComponent<SpriteRenderer>() };
    }

    private void Animate()
    {
        if (active.Count == 0)
            return;

        if (mainCamera == null)
            mainCamera = Camera.main;

        Quaternion facing = Quaternion.identity;
        if (mainCamera != null)
        {
            Vector3 camForward = mainCamera.transform.forward;
            camForward.y = 0f;
            if (camForward.sqrMagnitude > 1e-4f)
                facing = Quaternion.LookRotation(camForward.normalized, Vector3.up);
        }

        float lifetime = Lifetime;

        for (int i = active.Count - 1; i >= 0; i--)
        {
            Puff puff = active[i];
            puff.age += Time.deltaTime;
            float t = puff.age / lifetime;

            if (t >= 1f)
            {
                Recycle(i);
                continue;
            }

            // Shrinks toward its base, so it stays sitting on the floor. The fade starts gently
            // so the puff at the heels stays solid and only the tail of the trail goes faint.
            puff.transform.localScale = Vector3.one * puffScale * (1f - t);
            puff.transform.rotation = facing;

            Color c = puff.renderer.color;
            c.a = 1f - t * t;
            puff.renderer.color = c;
        }
    }
}
