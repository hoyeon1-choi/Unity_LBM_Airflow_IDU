using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SliceColorBarUI : MonoBehaviour
{
    public enum BarType
    {
        Velocity,
        Thermal
    }

    [Header("Mode")]
    [SerializeField] private BarType barType = BarType.Velocity;

    [Header("Visualizer References")]
    [SerializeField] private VelocityVisualizer velocityVisualizer;
    [SerializeField] private ThermalVisualizer thermalVisualizer;

    [Header("UI References")]
    [SerializeField] private RawImage colorBarImage;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text minValueText;
    [SerializeField] private TMP_Text maxValueText;

    [Header("LUT")]
    [SerializeField] private Texture2D lutTexture;

    [Header("Text")]
    [SerializeField] private string velocityTitle = "Velocity (m/s)";
    [SerializeField] private string thermalTitle = "Temperature (°C)";
    [SerializeField] private string numberFormat = "F2";

    [Header("Update Rate")]
    [Tooltip("Real-time interval for refreshing contour color-bar labels.")]
    [Min(0.05f)]
    [SerializeField] private float updateIntervalSeconds = 1.0f;

    [Header("Direction")]
    [SerializeField] private bool lowValueAtBottom = true;

    private float _nextUpdateRealtime;

    private void Start()
    {
        ApplyBarVisual();
        RefreshNow();
        _nextUpdateRealtime = Time.unscaledTime + Mathf.Max(updateIntervalSeconds, 0.05f);
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextUpdateRealtime)
            return;

        _nextUpdateRealtime = Time.unscaledTime + Mathf.Max(updateIntervalSeconds, 0.05f);
        RefreshNow();
    }

    public void RefreshNow()
    {
        ApplyBarVisual();

        if (barType == BarType.Velocity)
        {
            if (velocityVisualizer == null) return;

            if (titleText != null)
                titleText.text = velocityTitle;

            if (minValueText != null)
                minValueText.text = velocityVisualizer.CurrentDisplayMin.ToString(numberFormat);

            if (maxValueText != null)
                maxValueText.text = velocityVisualizer.CurrentDisplayMax.ToString(numberFormat);
        }
        else
        {
            if (thermalVisualizer == null) return;

            if (titleText != null)
                titleText.text = thermalTitle;

            if (minValueText != null)
                minValueText.text = thermalVisualizer.CurrentTempMinDegC.ToString(numberFormat);

            if (maxValueText != null)
                maxValueText.text = thermalVisualizer.CurrentTempMaxDegC.ToString(numberFormat);
        }
    }

    private void ApplyBarVisual()
    {
        if (colorBarImage == null)
            return;

        colorBarImage.texture = lutTexture;
        colorBarImage.color = Color.white;

        // 컬러바 오브젝트 자체는 회전하지 않음
        colorBarImage.rectTransform.localEulerAngles = Vector3.zero;

        // 상하 반전 여부를 머티리얼에 전달
        if (colorBarImage.material != null && colorBarImage.material.HasProperty("_FlipVertical"))
        {
            colorBarImage.material.SetFloat("_FlipVertical", lowValueAtBottom ? 0f : 1f);
        }
    }
}
