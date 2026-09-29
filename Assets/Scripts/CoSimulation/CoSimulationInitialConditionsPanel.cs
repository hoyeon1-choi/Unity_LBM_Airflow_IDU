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
[AddComponentMenu("Co-Simulation/Initial Conditions Panel")]
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

    private static readonly string[] ModeLabels = { "냉방", "제습", "난방" };
    private static readonly string[] FanLabels = { "미풍", "약", "중", "강", "파워" };
    private static readonly string[] DirectionLabels =
    {
        "P1\n15°", "P2\n30°", "P3\n45°", "P4\n60°", "P5\n75°", "P6\n90°"
    };

    private string indoorTemperatureText;
    private string indoorHumidityText;
    private string outdoorTemperatureText;
    private string outdoorHumidityText;
    private string setTemperatureText;
    private bool powerOn;
    private int operationModeIndex;
    private int fanStrengthIndex;
    private int windDirectionIndex;
    private string validationMessage = string.Empty;
    private bool relevantSceneFound;
    private bool applicationStartupScene;
    private Rect windowRect;
    private GUIStyle titleStyle;
    private GUIStyle sectionStyle;
    private GUIStyle errorStyle;
    private GUIStyle hintStyle;
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
        windowRect = new Rect(0.0f, 0.0f, 610.0f, 600.0f);
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
        windowRect.width = Mathf.Min(610.0f, Screen.width - 24.0f);
        windowRect.height = 600.0f;
        windowRect.x = (Screen.width - windowRect.width) * 0.5f;
        windowRect.y = Mathf.Max(12.0f, (Screen.height - windowRect.height) * 0.5f);

        GUI.depth = -10000;
        Color previousColor = GUI.color;
        GUI.color = new Color(0.02f, 0.03f, 0.05f, 0.98f);
        GUI.DrawTexture(new Rect(0.0f, 0.0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = previousColor;
        windowRect = GUI.ModalWindow(GetInstanceID(), windowRect, DrawWindow, "시뮬레이션 초기 조건");
    }

    private void DrawWindow(int id)
    {
        GUILayout.Space(8.0f);
        GUILayout.Label("시뮬레이션 시작 전에 기본 조건을 확인해 주세요.", titleStyle);
        GUILayout.Space(10.0f);

        DrawNumericField("실내온도", ref indoorTemperatureText, "℃");
        DrawNumericField("실내상대습도", ref indoorHumidityText, "%");
        DrawNumericField("실외온도", ref outdoorTemperatureText, "℃");
        DrawNumericField("실외상대습도", ref outdoorHumidityText, "%");

        GUILayout.Space(8.0f);
        powerOn = DrawToggleRow("R1(LBM) 전원", powerOn, powerOn ? "On" : "Off");

        GUILayout.Space(10.0f);
        GUILayout.Label("제품 운전 설정", sectionStyle);
        bool previousEnabled = GUI.enabled;
        GUI.enabled = powerOn;
        DrawNumericField("설정온도", ref setTemperatureText, "℃");
        GUILayout.Label("운전모드", hintStyle);
        operationModeIndex = GUILayout.SelectionGrid(operationModeIndex, ModeLabels, 3, GUILayout.Height(34.0f));
        GUILayout.Label("바람세기", hintStyle);
        fanStrengthIndex = GUILayout.SelectionGrid(fanStrengthIndex, FanLabels, 5, GUILayout.Height(34.0f));
        GUILayout.Label("바람방향 (천장면과 취출기류의 각도)", hintStyle);
        windDirectionIndex = GUILayout.SelectionGrid(windDirectionIndex, DirectionLabels, 6, GUILayout.Height(48.0f));
        GUI.enabled = previousEnabled;
        if (!powerOn)
            GUILayout.Label("R1 전원을 On으로 전환하면 위 제품 운전 설정을 변경할 수 있습니다. R2~R5는 통합창에서 제어합니다.", hintStyle);

        if (!string.IsNullOrEmpty(validationMessage))
            GUILayout.Label(validationMessage, errorStyle);

        if (!relevantSceneFound)
            GUILayout.Label("\uC2DC\uBBAC\uB808\uC774\uC158 Scene\uC744 \uBD88\uB7EC\uC624\uB294 \uC911\uC785\uB2C8\uB2E4...", hintStyle);

        GUILayout.FlexibleSpace();
        bool previousButtonEnabled = GUI.enabled;
        GUI.enabled = previousButtonEnabled && relevantSceneFound;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("기본값 복원", GUILayout.Height(38.0f)))
            ResetDefaults();
        if (GUILayout.Button("조건 적용 후 시뮬레이션 시작", GUILayout.Height(38.0f)))
            TryApplyAndStart();
        GUILayout.EndHorizontal();
        GUI.enabled = previousButtonEnabled;
    }

    private void DrawNumericField(string label, ref string value, string unit)
    {
        GUILayout.BeginHorizontal(GUILayout.Height(32.0f));
        GUILayout.Label(label, GUILayout.Width(185.0f));
        value = GUILayout.TextField(value, GUILayout.Width(170.0f));
        GUILayout.Label(unit, GUILayout.Width(40.0f));
        GUILayout.EndHorizontal();
    }

    private static bool DrawToggleRow(string label, bool value, string state)
    {
        GUILayout.BeginHorizontal(GUILayout.Height(32.0f));
        GUILayout.Label(label, GUILayout.Width(185.0f));
        bool updated = GUILayout.Toggle(value, state, GUILayout.Width(170.0f));
        GUILayout.EndHorizontal();
        return updated;
    }

    private void TryApplyAndStart()
    {
        if (!relevantSceneFound)
            return;

        if (!TryParse(indoorTemperatureText, out float indoorTemperature) ||
            !TryParse(indoorHumidityText, out float indoorHumidity) ||
            !TryParse(outdoorTemperatureText, out float outdoorTemperature) ||
            !TryParse(outdoorHumidityText, out float outdoorHumidity) ||
            !TryParse(setTemperatureText, out float setTemperature))
        {
            validationMessage = "온도와 습도는 숫자로 입력해 주세요.";
            return;
        }

        if (setTemperature < -30.0f || setTemperature > 60.0f)
        {
            validationMessage = "설정온도는 -30~60℃ 범위로 입력해 주세요.";
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
        float dischargeAngle = (windDirectionIndex + 1) * 15.0f;

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
            $"[Initial Conditions][{tag}] room={indoorTemperature:F1}C/{indoorHumidity:F1}%, " +
            $"outdoor={outdoorTemperature:F1}C/{outdoorHumidity:F1}%, R1 power={(powerOn ? "On" : "Off")}, R2-R5 power=Off, " +
            $"setTemperature={setTemperature:F1}C, mode={ModeLabels[operationModeIndex]}, fan={FanLabels[fanStrengthIndex]}, " +
            $"direction=P{windDirectionIndex + 1} ({dischargeAngle:F0} deg). Simulation started.");

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
        setTemperatureText = DefaultSetTemperatureDegC.ToString("0.0", CultureInfo.InvariantCulture);
        powerOn = false;
        operationModeIndex = (int)OperationMode.Cooling;
        fanStrengthIndex = (int)FanStrength.High - 1;
        windDirectionIndex = 2;
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
        GUI.skin.font = runtimeFont;
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
        if (runtimeFont != null)
            Destroy(runtimeFont);
    }
}
