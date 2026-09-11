using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One glass in the water-pouring minigame: how much is in it, and everything that
/// shows that amount — the liquid itself, the ml readout, and the indicator bar.
///
/// The liquid is drawn by a Custom/UILiquid material on <see cref="liquidImage"/>. That
/// material is instanced per cup in Awake, so three glasses on screen can hold three
/// different amounts; sharing one material would make them all move together.
/// </summary>
public class WaterCup : MonoBehaviour
{
    [Header("Liquid")]
    [Tooltip("Image using the Custom/UILiquid material, sized to the inside of the glass " +
             "and drawn behind the glass sprite.")]
    [SerializeField] private Image liquidImage;
    [Tooltip("Fill the shader reaches at full. Below 1 because the glass sprite's rim sits " +
             "above the water line even when the glass is full.")]
    [SerializeField, Range(0.5f, 1f)] private float fullLiquidLevel = 0.94f;

    [Header("Readout")]
    [SerializeField] private TextMeshProUGUI amountLabel;
    [Tooltip("Bar inside the indicator frame. Needs Image Type = Filled, Horizontal.")]
    [SerializeField] private Image indicatorFill;

    [Header("Capacity")]
    [SerializeField] private float capacityMl = 500f;

    [Header("Sloshing")]
    [Tooltip("Wave height while water is landing in the glass.")]
    [SerializeField] private float pouringWaveAmp = 0.05f;
    [Tooltip("Wave height once the surface has settled.")]
    [SerializeField] private float restingWaveAmp = 0.012f;
    [Tooltip("How quickly the surface settles after pouring stops. Higher is snappier.")]
    [SerializeField] private float settleSpeed = 3.5f;

    [Header("Full")]
    [Tooltip("Tint the readout takes once the glass is full. White by default, so a full " +
             "glass reads the same as any other — the bar and the water already show it.")]
    [SerializeField] private Color fullLabelColor = Color.white;

    private static readonly int FillId = Shader.PropertyToID("_Fill");
    private static readonly int WaveAmpId = Shader.PropertyToID("_WaveAmp");

    // Instanced in Awake so each glass drives its own fill level
    private Material liquidMaterial;

    private Color idleLabelColor = Color.white;

    private float currentMl = 0f;

    // Eased towards the pouring or resting amplitude rather than snapping, so the surface
    // keeps moving for a moment after the player lets go.
    private float waveAmp;

    private bool isPouring = false;

    public float CapacityMl => capacityMl;
    public float CurrentMl => currentMl;
    public bool IsFull => currentMl >= capacityMl;
    public float NormalizedFill => capacityMl > 0f ? Mathf.Clamp01(currentMl / capacityMl) : 0f;

    private void Awake()
    {
        if (liquidImage != null && liquidImage.material != null)
        {
            liquidMaterial = new Material(liquidImage.material);
            liquidImage.material = liquidMaterial;
        }

        if (amountLabel != null)
            idleLabelColor = amountLabel.color;

        waveAmp = restingWaveAmp;

        ResetCup();
    }

    private void OnDestroy()
    {
        if (liquidMaterial != null)
            Destroy(liquidMaterial);
    }

    private void Update()
    {
        // Unscaled: the quiz runs with the game timer paused.
        float target = isPouring ? pouringWaveAmp : restingWaveAmp;
        waveAmp = Mathf.Lerp(waveAmp, target, 1f - Mathf.Exp(-settleSpeed * Time.unscaledDeltaTime));

        if (liquidMaterial != null)
            liquidMaterial.SetFloat(WaveAmpId, waveAmp);
    }

    /// <summary>
    /// Empties the glass and puts every readout back to zero.
    /// </summary>
    public void ResetCup()
    {
        currentMl = 0f;
        isPouring = false;
        waveAmp = restingWaveAmp;

        if (amountLabel != null)
            amountLabel.color = idleLabelColor;

        ApplyFill();
    }

    /// <summary>
    /// Adds water and returns true if that filled the glass.
    /// Overpouring is clamped rather than penalised — the glass simply stops at full.
    /// </summary>
    public bool AddMilliliters(float amount)
    {
        if (IsFull)
            return false;

        currentMl = Mathf.Min(capacityMl, currentMl + amount);
        ApplyFill();

        if (!IsFull)
            return false;

        if (amountLabel != null)
            amountLabel.color = fullLabelColor;

        return true;
    }

    /// <summary>
    /// Tells the glass whether water is currently landing in it, which is what drives
    /// how hard the surface sloshes.
    /// </summary>
    public void SetPouring(bool pouring)
    {
        isPouring = pouring;
    }

    /// <summary>
    /// Where the water line sits right now, in this cup's own rect space (0 at the
    /// bottom of the liquid area, 1 at the top). The pour stream uses it to land the
    /// splash on the surface instead of the floor of the glass.
    /// </summary>
    public float GetSurfaceHeightNormalized()
    {
        return NormalizedFill * fullLiquidLevel;
    }

    /// <summary>
    /// The liquid rect, so the stream can work out where the surface is on screen.
    /// </summary>
    public RectTransform GetLiquidRect()
    {
        return liquidImage != null ? liquidImage.rectTransform : (RectTransform)transform;
    }

    private void ApplyFill()
    {
        if (liquidMaterial != null)
            liquidMaterial.SetFloat(FillId, GetSurfaceHeightNormalized());

        if (indicatorFill != null)
            indicatorFill.fillAmount = NormalizedFill;

        if (amountLabel != null)
            amountLabel.text = $"{Mathf.RoundToInt(currentMl)}/{Mathf.RoundToInt(capacityMl)} ml";
    }
}
