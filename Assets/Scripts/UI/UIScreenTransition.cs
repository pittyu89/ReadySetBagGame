using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Animates a screen's elements in when it is enabled (slide from an offset + fade, each with
/// its own delay), and plays that same timeline backwards on request before the screen is left.
/// </summary>
public class UIScreenTransition : MonoBehaviour
{
    [Serializable]
    public class Element
    {
        public RectTransform target;
        [Tooltip("Where the element starts, relative to its resting position.")]
        public Vector2 offset;
        public bool fade = true;
        [Tooltip("Seconds after the screen opens before this element starts moving.")]
        public float delay;
    }

    [SerializeField] private Element[] elements = new Element[] { };
    [SerializeField] private float duration = 0.45f;
    [SerializeField] private bool playOnEnable = true;

    private Vector2[] homePositions;
    private CanvasGroup[] groups;
    private CanvasGroup rootGroup;
    private Coroutine running;
    private bool skipNextPlayIn;

    /// <summary>True while the screen is animating in or out.</summary>
    public bool IsPlaying => running != null;

    void Awake()
    {
        rootGroup = GetComponent<CanvasGroup>();
        if (rootGroup == null)
            rootGroup = gameObject.AddComponent<CanvasGroup>();

        // Resting positions are the authored ones - captured before anything moves them
        homePositions = new Vector2[elements.Length];
        groups = new CanvasGroup[elements.Length];

        for (int i = 0; i < elements.Length; i++)
        {
            RectTransform target = elements[i].target;
            if (target == null)
                continue;

            homePositions[i] = target.anchoredPosition;

            if (elements[i].fade && target.gameObject != gameObject)
            {
                groups[i] = target.GetComponent<CanvasGroup>();
                if (groups[i] == null)
                    groups[i] = target.gameObject.AddComponent<CanvasGroup>();
            }
            else if (elements[i].fade)
            {
                groups[i] = rootGroup;
            }
        }
    }

    void OnEnable()
    {
        if (skipNextPlayIn)
        {
            skipNextPlayIn = false;
            ShowImmediately();
            return;
        }

        if (playOnEnable)
            PlayIn();
    }

    /// <summary>
    /// The next time this screen is enabled it appears already in place, with no animation.
    /// </summary>
    public void SkipNextPlayIn()
    {
        skipNextPlayIn = true;
    }

    /// <summary>Snaps every element to its resting position, fully visible.</summary>
    public void ShowImmediately()
    {
        if (running != null)
        {
            StopCoroutine(running);
            running = null;
        }

        Apply(Length());
        rootGroup.blocksRaycasts = true;
    }

    void OnDisable()
    {
        running = null;
        rootGroup.blocksRaycasts = true;
    }

    public void PlayIn()
    {
        if (!isActiveAndEnabled)
            return;

        if (running != null)
            StopCoroutine(running);

        running = StartCoroutine(Run(false, null));
    }

    /// <summary>
    /// Plays the in-animation backwards, then runs <paramref name="onComplete"/>.
    /// </summary>
    public void PlayOut(Action onComplete)
    {
        if (!isActiveAndEnabled)
        {
            onComplete?.Invoke();
            return;
        }

        if (running != null)
            StopCoroutine(running);

        running = StartCoroutine(Run(true, onComplete));
    }

    private IEnumerator Run(bool reverse, Action onComplete)
    {
        rootGroup.blocksRaycasts = false;

        float length = Length();
        float time = reverse ? length : 0f;
        Apply(time);

        while (reverse ? time > 0f : time < length)
        {
            yield return null;
            // Capped like the main menu intro so a hitch doesn't skip the motion
            float step = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            time = reverse ? Mathf.Max(time - step, 0f) : Mathf.Min(time + step, length);
            Apply(time);
        }

        running = null;
        rootGroup.blocksRaycasts = true;
        onComplete?.Invoke();
    }

    private float Length()
    {
        float longest = 0f;
        foreach (Element element in elements)
            longest = Mathf.Max(longest, element.delay + duration);
        return longest;
    }

    /// <summary>Poses every element as it looks <paramref name="time"/> seconds into the in-animation.</summary>
    private void Apply(float time)
    {
        for (int i = 0; i < elements.Length; i++)
        {
            Element element = elements[i];
            if (element.target == null)
                continue;

            float progress = duration > 0f ? Mathf.Clamp01((time - element.delay) / duration) : 1f;

            element.target.anchoredPosition = homePositions[i] + element.offset * (1f - EaseOutQuart(progress));

            if (groups[i] != null)
                groups[i].alpha = SmoothStep(progress);
        }
    }

    private static float SmoothStep(float t) => t * t * (3f - 2f * t);

    private static float EaseOutQuart(float t)
    {
        float inv = 1f - t;
        return 1f - inv * inv * inv * inv;
    }
}
