using UnityEngine;
using UnityEngine.UI;

public sealed class Client_TaggerStunnedOverlayView : MonoBehaviour
{
    private static readonly int CenterId = Shader.PropertyToID("_Center");
    private static readonly int RadiusId = Shader.PropertyToID("_Radius");
    private static readonly int TimeValueId = Shader.PropertyToID("_TimeValue");
    private static readonly int OverlayColorId = Shader.PropertyToID("_OverlayColor");
    private static readonly int FeatherId = Shader.PropertyToID("_Feather");
    private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
    private static readonly int NoiseStrengthId = Shader.PropertyToID("_NoiseStrength");
    private static readonly int NoiseSpeedId = Shader.PropertyToID("_NoiseSpeed");
    private static readonly int DitherStrengthId = Shader.PropertyToID("_DitherStrength");

    [SerializeField] private Image overlayImage;
    [SerializeField] private Color overlayColor = new Color(0f, 0f, 0f, 0.92f);
    [SerializeField] private float hiddenRadius = 1.5f;
    [SerializeField] private float stunnedRadius = 0.18f;
    [SerializeField] private float showDuration = 0.6f;
    [SerializeField] private float hideDuration = 0.45f;
    [SerializeField] private float feather = 0.08f;
    [SerializeField] private float noiseScale = 38f;
    [SerializeField] private float noiseStrength = 0.035f;
    [SerializeField] private float noiseSpeed = 1.4f;
    [SerializeField, Range(0f, 1f)] private float ditherStrength = 0.35f;

    private Material runtimeMaterial;
    private Vector2 centerUv = new Vector2(0.5f, 0.5f);
    private float currentRadius;
    private float fromRadius;
    private float targetRadius;
    private float transitionDuration;
    private float transitionTimer;
    private bool isTransitioning;

    public float HideDuration => Mathf.Max(0f, hideDuration);

    // - Role: Apply shader values received from the router.
    public void SetCenter(Vector2 nextCenterUv)
    {
        centerUv = new Vector2(
            Mathf.Clamp01(nextCenterUv.x),
            Mathf.Clamp01(nextCenterUv.y));
        EnsureMaterial();
        ApplyAllProperties();
    }

    // - Role: Start visible overlay animation.
    public void Show()
    {
        EnsureMaterial();
        BeginRadiusTransition(hiddenRadius, stunnedRadius, Mathf.Max(0f, showDuration));
    }

    // - Role: Start hidden overlay animation.
    public void Hide()
    {
        EnsureMaterial();
        BeginRadiusTransition(currentRadius, hiddenRadius, Mathf.Max(0f, hideDuration));
    }

    private void Awake()
    {
        currentRadius = hiddenRadius;
        ApplyAllProperties();
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null)
        {
            Destroy(runtimeMaterial);
            runtimeMaterial = null;
        }
    }

    private void Update()
    {
        UpdateRadiusTransition();
        ApplyAllProperties();
    }

    private void OnValidate()
    {
        hiddenRadius = Mathf.Max(0f, hiddenRadius);
        stunnedRadius = Mathf.Max(0f, stunnedRadius);
        showDuration = Mathf.Max(0f, showDuration);
        hideDuration = Mathf.Max(0f, hideDuration);
        feather = Mathf.Max(0.0001f, feather);
        noiseScale = Mathf.Max(0f, noiseScale);
        noiseStrength = Mathf.Max(0f, noiseStrength);
    }

    private void EnsureMaterial()
    {
        if (overlayImage == null || runtimeMaterial != null)
        {
            return;
        }

        Material sourceMaterial = overlayImage.material;
        if (sourceMaterial == null)
        {
            return;
        }

        runtimeMaterial = Instantiate(sourceMaterial);
        overlayImage.material = runtimeMaterial;
    }

    private void BeginRadiusTransition(float startRadius, float endRadius, float duration)
    {
        fromRadius = Mathf.Max(0f, startRadius);
        targetRadius = Mathf.Max(0f, endRadius);
        transitionDuration = Mathf.Max(0.0001f, duration);
        transitionTimer = 0f;
        isTransitioning = true;
        currentRadius = fromRadius;
        ApplyAllProperties();

        if (duration <= 0f)
        {
            isTransitioning = false;
            currentRadius = targetRadius;
            ApplyAllProperties();
        }
    }

    private void UpdateRadiusTransition()
    {
        if (!isTransitioning)
        {
            return;
        }

        transitionTimer += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(transitionTimer / transitionDuration);
        float easedT = t * t * (3f - 2f * t);
        currentRadius = Mathf.Lerp(fromRadius, targetRadius, easedT);

        if (t >= 1f)
        {
            isTransitioning = false;
            currentRadius = targetRadius;
        }
    }

    private void ApplyAllProperties()
    {
        EnsureMaterial();
        if (runtimeMaterial == null)
        {
            return;
        }

        runtimeMaterial.SetVector(CenterId, new Vector4(centerUv.x, centerUv.y, 0f, 0f));
        runtimeMaterial.SetFloat(RadiusId, Mathf.Max(0f, currentRadius));
        runtimeMaterial.SetFloat(TimeValueId, Time.unscaledTime);
        runtimeMaterial.SetColor(OverlayColorId, overlayColor);
        runtimeMaterial.SetFloat(FeatherId, Mathf.Max(0.0001f, feather));
        runtimeMaterial.SetFloat(NoiseScaleId, Mathf.Max(0f, noiseScale));
        runtimeMaterial.SetFloat(NoiseStrengthId, Mathf.Max(0f, noiseStrength));
        runtimeMaterial.SetFloat(NoiseSpeedId, noiseSpeed);
        runtimeMaterial.SetFloat(DitherStrengthId, Mathf.Clamp01(ditherStrength));
    }
}
