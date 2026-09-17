using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Leaves little pixel dust puffs at the character's feet while they walk. A puff is dropped
/// every <see cref="spacing"/> world units travelled, plays its frames while drifting up and
/// fading, then goes back to a small pool. Puffs face the camera like the character does.
/// </summary>
public class DustTrail : MonoBehaviour
{
    [Tooltip("The puff animation, in order.")]
    [SerializeField] private Sprite[] frames = new Sprite[] { };
    [Tooltip("Distance walked between puffs.")]
    [SerializeField] private float spacing = 0.6f;
    [Tooltip("Slower than this (units/second) counts as standing still.")]
    [SerializeField] private float minSpeed = 0.5f;
    [SerializeField] private float lifetime = 0.4f;
    [SerializeField] private float puffScale = 1.5f;
    [Tooltip("How far a puff rises over its life.")]
    [SerializeField] private float rise = 0.12f;
    [Tooltip("Puffs appear this far behind the feet, against the direction of travel.")]
    [SerializeField] private float behindOffset = 0.15f;
    [Tooltip("Tiny lift so puffs don't clip into the floor.")]
    [SerializeField] private float floorLift = 0.04f;

    private class Puff
    {
        public Transform transform;
        public SpriteRenderer renderer;
        public Vector3 start;
        public float age;
    }

    private readonly List<Puff> active = new List<Puff>();
    private readonly Stack<Puff> pool = new Stack<Puff>();
    private CharacterController controller;
    private Camera mainCamera;
    private Vector3 lastPosition;
    private float travelled;
    private Transform puffParent;

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

        if (frames.Length > 0 && grounded && speed >= minSpeed)
        {
            travelled += step.magnitude;
            if (travelled >= spacing)
            {
                travelled = 0f;
                Spawn(FeetPosition() - step.normalized * behindOffset);
            }
        }
        else
        {
            // The first step after stopping kicks up a puff straight away
            travelled = spacing;
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
        Puff puff = pool.Count > 0 ? pool.Pop() : CreatePuff();
        puff.start = position;
        puff.age = 0f;
        puff.transform.position = position;
        puff.transform.localScale = Vector3.one * puffScale;
        puff.renderer.sprite = frames[0];
        puff.renderer.color = Color.white;
        puff.transform.gameObject.SetActive(true);
        active.Add(puff);
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

        for (int i = active.Count - 1; i >= 0; i--)
        {
            Puff puff = active[i];
            puff.age += Time.deltaTime;
            float t = puff.age / lifetime;

            if (t >= 1f)
            {
                puff.transform.gameObject.SetActive(false);
                active.RemoveAt(i);
                pool.Push(puff);
                continue;
            }

            puff.renderer.sprite = frames[Mathf.Min(frames.Length - 1, Mathf.FloorToInt(t * frames.Length))];
            puff.transform.position = puff.start + Vector3.up * rise * t;
            puff.transform.rotation = facing;

            // Solid for the first half, then fade out
            Color c = puff.renderer.color;
            c.a = t < 0.5f ? 1f : 1f - (t - 0.5f) * 2f;
            puff.renderer.color = c;
        }
    }
}
