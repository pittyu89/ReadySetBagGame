using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Full-screen catcher for the timing tap in the gloves minigame.
///
/// While the ring is closing the tap can land anywhere — the player is watching the ring,
/// not aiming — so this lies over the whole panel and is only armed for that window.
/// Counted on press rather than release, or every tap would land a beat late.
/// </summary>
[RequireComponent(typeof(Graphic))]
public class GlovesTapArea : MonoBehaviour, IPointerDownHandler
{
    /// <summary>Raised once per press, and only while armed.</summary>
    public event Action Tapped;

    /// <summary>The same press, with where on the screen it landed.</summary>
    public event Action<Vector2> TappedAt;

    private Graphic graphic;
    private bool isArmed = false;

    private void Awake()
    {
        graphic = GetComponent<Graphic>();
        SetArmed(false);
    }

    /// <summary>
    /// Only the armed catcher swallows presses; otherwise it would sit over the shards and
    /// eat every press aimed at them.
    /// </summary>
    public void SetArmed(bool armed)
    {
        isArmed = armed;

        if (graphic == null)
            graphic = GetComponent<Graphic>();

        if (graphic != null)
            graphic.raycastTarget = armed;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!isArmed)
            return;

        Tapped?.Invoke();
        TappedAt?.Invoke(eventData.position);
    }
}
