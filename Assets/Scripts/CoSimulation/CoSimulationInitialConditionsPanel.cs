using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CoSimulationStartupGate
{
    private static bool waitingForConfirmation = true;

    public static bool IsWaitingForConfirmation =>
        !Application.isBatchMode && waitingForConfirmation;

    public static void Reset()
    {
        waitingForConfirmation = !Application.isBatchMode;
    }

    public static void Confirm()
    {
        waitingForConfirmation = false;
    }
}

[DefaultExecutionOrder(-32700)]
[AddComponentMenu("Co-Simulation/Simulation Set-up Panel")]
public sealed class CoSimulationInitialConditionsPanel : MonoBehaviour
{
    public enum OperationMode
    {
        Cooling = 0,
        Dehumidification = 1,
        Heating = 2
    }

    public enum FanStrength
    {
        VeryLow = 1,
        Low = 2,
        Medium = 3,
        High = 4,
        SuperHigh = 5
    }

    private const float DefaultIndoorTemperatureDegC = 30.0f;
    private const float DefaultIndoorHumidityPercent = 50.0f;
    private const float DefaultOutdoorTemperatureDegC = 35.0f;
    private const float DefaultOutdoorHumidityPercent = 70.0f;
    private const float DefaultSetTemperatureDegC = 28.0f;
    private const float DefaultTargetSimulationTimeSeconds = 30.0f;

    private string indoorTemperatureText;
    private string indoorHumidityText;
    private string outdoorTemperatureText;
    private string outdoorHumidityText;
    private string targetSimulationTimeText;
    private string validationMessage = string.Empty;
    private bool relevantSceneFound;
    private bool applicationStartupScene;
    private Rect windowRect;
    private Vector2 scrollPosition;
    private GUIStyle titleStyle;
    private GUIStyle sectionStyle;
    private GUIStyle errorStyle;
    private GUIStyle hintStyle;
    private GUIStyle bodyStyle;
    private GUIStyle buttonStyle;
    private Font runtimeFont;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateBeforeSceneLoad()
    {
        CoSimulationStartupGate.Reset();
        if (Application.isBatchMode)
            return;

        GameObject host = new GameObject("__CoSimulationInitialConditions");
        DontDestroyOnLoad(host);
        host.AddComponent<CoSimulationInitialConditionsPanel>();
    }

    private void Awake()
    {
        ResetDefaults();
        windowRect = new Rect(0.0f, 0.0f, 610.0f, 470.0f);
        string activeScenePath = SceneManager.GetActiveScene().path ?? string.Empty;
        applicationStartupScene =
            activeScenePath.EndsWith("ApplicationBootstrap.unity", StringComparison.OrdinalIgnoreCase) ||
            activeScenePath.EndsWith("LBM_1wayCST.unity", StringComparison.OrdinalIgnoreCase);
    }

    private void Update()
    {
        if (!CoSimulationStartupGate.IsWaitingForConfirmation)
            return;

        if (!relevantSceneFound)
        {
            // Do not access SimulationController.Instance while the LBM scene is still loading.
            // The generic Singleton getter creates an empty fallback object when none exists,
            // which would then replace the correctly configured scene controller.
            relevantSceneFound = FindConfiguredSimulationController() != null;
        }
    }

    private void OnGUI()
    {
        if (!CoSimulationStartupGate.IsWaitingForConfirmation ||
            (!applicationStartupScene && !relevantSceneFound))
            return;

        EnsureStyles();
        windowRect.width = Mathf.Min(610.0f, Mathf.Max(300.0f, Screen.width - 24.0f));
        windowRect.height = Mathf.Min(470.0f, Mathf.Max(300.0f, Screen.height - 24.0f));
        windowRect.x = (Screen.width - windowRect.width) * 0.5f;
        windowRect.y = Mathf.Max(12.0f, (Screen.height - windowRect.height) * 0.5f);

        GUI.depth = -10000;
        Color previousColor = GUI.color;
        GUI.color = new Color(0.02f, 0.03f, 0.05f, 0.98f);
        GUI.DrawTexture(new Rect(0.0f, 0.0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = previousColor;
        windowRect = GUI.ModalWindow(GetInstanceID(), windowRect, DrawWindow, "Simulation Set-up");
    }

    private void DrawWindow(int id)
    {
        GUILayout.Space(8.0f);
        GUILayout.Label("시뮬레이션 시작 전에 기본 조건을 확인해 주세요.", titleStyle);
        GUILayout.Space(10.0f);

        scrollPosition = GUILayout.BeginScrollView(
            scrollPosition,
            false,
            true,
            GUILayout.ExpandHeight(true));

        DrawNumericField("실내온도", ref indoorTemperatureText, "℃");
        DrawNumericField("실내상대습도", ref indoorHumidityText, "%");
        DrawNumericField("실외온도", ref outdoorTemperatureText, "℃");
        DrawNumericField("실외상대습도", ref outdoorHumidityText, "%");

        GUILayout.Space(10.0f);
        GUILayout.Label("Simulation", sectionStyle);
        DrawNumericField("Target simulation time", ref targetSimulationTimeText, "s");
        GUILayout.Label("제품 운전 조건은 시뮬레이션 시작 후 오른쪽 모니터링 창에서 변경합니다.", hintStyle);

        if (!string.IsNullOrEmpty(validationMessage))
            GUILayout.Label(validationMessage, errorStyle);

        if (!relevantSceneFound)
            GUILayout.Label("\uC2DC\uBBAC\uB808\uC774\uC158 Scene\uC744 \uBD88\uB7EC\uC624\uB294 \uC911\uC785\uB2C8\uB2E4...", hintStyle);

        GUILayout.EndScrollView();
        GUILayout.Space(6.0f);
        bool previousButtonEnabled = GUI.enabled;
        GUI.enabled = previousButtonEnabled && relevantSceneFound;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("기본값 복원", buttonStyle, GUILayout.Height(38.0f)))
            ResetDefaults();
        if (GUILayout.Button("조건 적용 후 시뮬레이션 시작", buttonStyle, GUILayout.Height(38.0f)))
            TryApplyAndStart();
        GUILayout.EndHorizontal();
        GUI.enabled = previousButtonEnabled;
    }

    private void DrawNumericField(string label, ref string value, string unit)
    {
        GUILayout.BeginHorizontal(GUILayout.Height(32.0f));
        GUILayout.Label(label, bodyStyle, GUILayout.Width(185.0f));
        value = GUILayout.TextField(value, GUILayout.Width(170.0f));
        GUILayout.Label(unit, bodyStyle, GUILayout.Width(40.0f));
        GUILayout.EndHorizontal();
    }

    private void TryApplyAndStart()
    {
        if (!relevantSceneFound)
            return;

        if (!TryParse(indoorTemperatureText, out float indoorTemperature) ||
            !TryParse(indoorHumidityText, out float indoorHumidity) ||
            !TryParse(outdoorTemperatureText, out float outdoorTemperature) ||
            !TryParse(outdoorHumidityText, out float outdoorHumidity) ||
            !TryParse(targetSimulationTimeText, out float targetSimulationTime))
        {
            validationMessage = "온도, 습도, 목표 시뮬레이션 시간은 숫자로 입력해 주세요.";
            return;
        }

        if (targetSimulationTime <= 0.0f)
        {
            validationMessage = "Target simulation time must be greater than zero.";
            return;
        }

        if (indoorTemperature < -30.0f || indoorTemperature > 60.0f ||
            outdoorTemperature < -50.0f || outdoorTemperature > 70.0f)
        {
            validationMessage = "온도 입력 범위를 확인해 주세요. (실내 -30~60℃, 실외 -50~70℃)";
            return;
        }

        if (indoorHumidity < 0.0f || indoorHumidity > 100.0f ||
            outdoorHumidity < 0.0f || outdoorHumidity > 100.0f)
        {
            validationMessage = "상대습도는 0~100% 범위로 입력해 주세요.";
            return;
        }

        SimulationController controller = FindConfiguredSimulationController();
        if (controller == null)
        {
            validationMessage = "SimulationController가 준비되지 않았습니다. 잠시 후 다시 시도해 주세요.";
            return;
        }

        CoSimulationOrchestrator orchestrator = FindFirstObjectByType<CoSimulationOrchestrator>();
        AirflowLbmSignalAdapter airflow = FindFirstObjectByType<AirflowLbmSignalAdapter>();
        float setTemperature = DefaultSetTemperatureDegC;
        // The setup screen no longer exposes product controls, so start R1 with
        // a valid cooling preset. The monitoring panel can turn it off later.
        bool powerOn = true;
        int operationModeIndex = (int)OperationMode.Cooling;
        int fanStrengthIndex = (int)FanStrength.High - 1;
        int windDirectionIndex = 2;
        float dischargeAngle = (windDirectionIndex + 1) * 15.0f;

        controller.SetTargetSimulationTime(targetSimulationTime);
        controller.SetCaseStudyExecutionEnabled(false);

        controller.ApplyInitialRoomTemperatureDegC(indoorTemperature);
        airflow?.ApplyInitialIndoorConditions(indoorTemperature, indoorHumidity);
        orchestrator?.ApplyStartupConditions(
            indoorTemperature,
            indoorHumidity,
            outdoorTemperature,
            outdoorHumidity,
            setTemperature,
            powerOn,
            operationModeIndex,
            fanStrengthIndex + 1,
            windDirectionIndex + 1,
            dischargeAngle);

        LBMZouHeBox controlledInlet = FindPrimaryInlet();
        if (controlledInlet != null)
        {
            controlledInlet.SetCeilingDischargeAngleDeg(dischargeAngle, false);
            controlledInlet.SetPower(powerOn, true);
        }

        controller.SyncDynamicBoundaryInputsNow();
        controller.SetSimulationRunning(true);
        CoSimulationStartupGate.Confirm();

        string tag = orchestrator != null ? orchestrator.ProfileName : controller.ActiveCaseName;
        Debug.Log(
            $"[Simulation Set-up][{tag}] room={indoorTemperature:F1}C/{indoorHumidity:F1}%, " +
            $"outdoor={outdoorTemperature:F1}C/{outdoorHumidity:F1}%, R1 power={(powerOn ? "On" : "Off")}, R2-R5 power=Off, " +
            $"remoteDefaults=setTemperature={setTemperature:F1}C/mode={operationModeIndex}/fan={fanStrengthIndex + 1}/" +
            $"direction=P{windDirectionIndex + 1} ({dischargeAngle:F0} deg), " +
            $"targetTime={targetSimulationTime.ToString("F3", CultureInfo.InvariantCulture)}s. " +
            "Simulation started.");

        Destroy(gameObject);
    }

    private static LBMZouHeBox FindPrimaryInlet()
    {
        LBMZouHeBox[] boxes = FindObjectsByType<LBMZouHeBox>(FindObjectsSortMode.InstanceID);
        LBMZouHeBox largest = null;
        for (int i = 0; i < boxes.Length; i++)
        {
            LBMZouHeBox box = boxes[i];
            if (box == null || box.PatchKind != LBMZouHeBox.Kind.Inlet)
                continue;

            if (string.Equals(box.name, "InletBox", StringComparison.OrdinalIgnoreCase))
                return box;

            if (largest == null || box.PatchAreaPhysCached > largest.PatchAreaPhysCached)
                largest = box;
        }

        return largest;
    }

    private static SimulationController FindConfiguredSimulationController()
    {
        SimulationController[] controllers =
            FindObjectsByType<SimulationController>(FindObjectsSortMode.InstanceID);
        for (int i = 0; i < controllers.Length; i++)
        {
            SimulationController controller = controllers[i];
            if (controller != null && controller.DomainRoot != null)
                return controller;
        }

        return null;
    }

    private void ResetDefaults()
    {
        indoorTemperatureText = DefaultIndoorTemperatureDegC.ToString("0.0", CultureInfo.InvariantCulture);
        indoorHumidityText = DefaultIndoorHumidityPercent.ToString("0.0", CultureInfo.InvariantCulture);
        outdoorTemperatureText = DefaultOutdoorTemperatureDegC.ToString("0.0", CultureInfo.InvariantCulture);
        outdoorHumidityText = DefaultOutdoorHumidityPercent.ToString("0.0", CultureInfo.InvariantCulture);
        targetSimulationTimeText = DefaultTargetSimulationTimeSeconds.ToString("0.0", CultureInfo.InvariantCulture);
        validationMessage = string.Empty;
    }

    private static bool TryParse(string text, out float value)
    {
        return float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
               float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private void EnsureStyles()
    {
        if (titleStyle != null)
            return;

        runtimeFont = Font.CreateDynamicFontFromOSFont(
            new[] { "Malgun Gothic", "맑은 고딕", "Arial" }, 18);
        // Do not assign this transient font to the global GUI.skin. The Editor reuses that
        // skin while restoring script state, so destroying the font would leave a stale
        // native reference and warn on the next domain reload.
        bodyStyle = new GUIStyle(GUI.skin.label)
        {
            font = runtimeFont
        };
        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            font = runtimeFont
        };
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            font = runtimeFont,
            fontSize = 19,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        sectionStyle = new GUIStyle(GUI.skin.label)
        {
            font = runtimeFont,
            fontSize = 15,
            fontStyle = FontStyle.Bold
        };
        hintStyle = new GUIStyle(GUI.skin.label)
        {
            font = runtimeFont,
            fontSize = 13,
            wordWrap = true
        };
        errorStyle = new GUIStyle(hintStyle);
        errorStyle.normal.textColor = new Color(1.0f, 0.45f, 0.35f);
    }

    private void OnDestroy()
    {
        // Detach every local GUIStyle before releasing the transient font.
        if (titleStyle != null) titleStyle.font = null;
        if (sectionStyle != null) sectionStyle.font = null;
        if (errorStyle != null) errorStyle.font = null;
        if (hintStyle != null) hintStyle.font = null;
        if (bodyStyle != null) bodyStyle.font = null;
        if (buttonStyle != null) buttonStyle.font = null;

        if (runtimeFont != null)
            Destroy(runtimeFont);

        runtimeFont = null;
    }
}
