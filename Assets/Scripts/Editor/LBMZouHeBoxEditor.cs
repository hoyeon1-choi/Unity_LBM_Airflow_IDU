using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LBMZouHeBox))]
[CanEditMultipleObjects]
public class LBMZouHeBoxEditor : Editor
{
    private static readonly GUIContent BoundaryInputModeLabel =
        new GUIContent("Boundary Input Mode", "Shows only the input modes supported by the selected boundary type.");

    private static readonly GUIContent[] InletModeLabels =
    {
        new GUIContent("Velocity"),
        new GUIContent("Volume Flow Rate")
    };

    private static readonly int[] InletModeValues =
    {
        (int)LBMZouHeBox.BoundaryInputMode.Velocity,
        (int)LBMZouHeBox.BoundaryInputMode.VolumeFlowRate
    };

    private static readonly GUIContent[] OutletModeLabels =
    {
        new GUIContent("Pressure Density"),
        new GUIContent("Auto Mass Balanced Outlet")
    };

    private static readonly int[] OutletModeValues =
    {
        (int)LBMZouHeBox.BoundaryInputMode.PressureDensity,
        (int)LBMZouHeBox.BoundaryInputMode.AutoMassBalancedOutlet
    };

    private SerializedProperty _kindProp;
    private SerializedProperty _boundaryInputModeProp;
    private SerializedProperty _enableMassFluxCorrectionProp;

    private void OnEnable()
    {
        _kindProp = serializedObject.FindProperty("kind");
        _boundaryInputModeProp = serializedObject.FindProperty("boundaryInputMode");
        _enableMassFluxCorrectionProp = serializedObject.FindProperty("enableMassFluxCorrection");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;

        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;

            if (iterator.propertyPath == "m_Script")
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.PropertyField(iterator, true);
                continue;
            }

            if (iterator.propertyPath == "boundaryInputMode")
                continue;

            if (ShouldSkipProperty(iterator.propertyPath))
                continue;

            EditorGUILayout.PropertyField(iterator, true);

            if (iterator.propertyPath == "kind")
                DrawFilteredBoundaryInputMode();
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawFilteredBoundaryInputMode()
    {
        if (_kindProp == null || _boundaryInputModeProp == null)
            return;

        if (_kindProp.hasMultipleDifferentValues)
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.LabelField(BoundaryInputModeLabel, new GUIContent("Mixed boundary types"));
            return;
        }

        bool isInlet = _kindProp.enumValueIndex == (int)LBMZouHeBox.Kind.Inlet;
        GUIContent[] labels = isInlet ? InletModeLabels : OutletModeLabels;
        int[] values = isInlet ? InletModeValues : OutletModeValues;

        int currentValue = _boundaryInputModeProp.enumValueIndex;
        int selectedIndex = IndexOf(values, currentValue);
        if (selectedIndex < 0)
        {
            currentValue = GetDefaultModeValue(isInlet);
            _boundaryInputModeProp.enumValueIndex = currentValue;
            selectedIndex = 0;
            selectedIndex = IndexOf(values, currentValue);
        }

        EditorGUI.showMixedValue = _boundaryInputModeProp.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        int newIndex = EditorGUILayout.Popup(BoundaryInputModeLabel, selectedIndex, labels);
        bool changed = EditorGUI.EndChangeCheck();
        EditorGUI.showMixedValue = false;

        if (!changed)
            return;

        int newValue = values[Mathf.Clamp(newIndex, 0, values.Length - 1)];
        _boundaryInputModeProp.enumValueIndex = newValue;

        if (!isInlet && _enableMassFluxCorrectionProp != null)
        {
            _enableMassFluxCorrectionProp.boolValue =
                newValue == (int)LBMZouHeBox.BoundaryInputMode.AutoMassBalancedOutlet;
        }
    }

    private static int IndexOf(int[] values, int value)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == value)
                return i;
        }

        return -1;
    }

    private int GetDefaultModeValue(bool isInlet)
    {
        if (isInlet)
            return (int)LBMZouHeBox.BoundaryInputMode.Velocity;

        bool massCorrectionEnabled = _enableMassFluxCorrectionProp == null ||
                                     _enableMassFluxCorrectionProp.boolValue;

        return massCorrectionEnabled
            ? (int)LBMZouHeBox.BoundaryInputMode.AutoMassBalancedOutlet
            : (int)LBMZouHeBox.BoundaryInputMode.PressureDensity;
    }

    private bool ShouldSkipProperty(string propertyPath)
    {
        if (_kindProp == null || _kindProp.hasMultipleDifferentValues)
            return false;
        if (_boundaryInputModeProp != null && _boundaryInputModeProp.hasMultipleDifferentValues)
            return false;

        bool isInlet = _kindProp.enumValueIndex == (int)LBMZouHeBox.Kind.Inlet;
        int modeValue = _boundaryInputModeProp != null
            ? _boundaryInputModeProp.enumValueIndex
            : GetDefaultModeValue(isInlet);

        if (isInlet)
            return ShouldSkipInletProperty(propertyPath, modeValue);

        return ShouldSkipOutletProperty(propertyPath);
    }

    private static bool ShouldSkipInletProperty(string propertyPath, int modeValue)
    {
        if (IsOutletOnlyProperty(propertyPath))
            return true;

        bool isVolumeFlowMode = modeValue == (int)LBMZouHeBox.BoundaryInputMode.VolumeFlowRate;

        if (propertyPath == "windSpeedPhys")
            return isVolumeFlowMode;

        if (IsVolumeFlowProperty(propertyPath))
            return !isVolumeFlowMode;

        return false;
    }

    private static bool ShouldSkipOutletProperty(string propertyPath)
    {
        return IsInletOnlyProperty(propertyPath);
    }

    private static bool IsInletOnlyProperty(string propertyPath)
    {
        return propertyPath == "windSpeedPhys" ||
               propertyPath == "inletTemperatureDegC" ||
               propertyPath == "inletTemperatureLBM" ||
               IsVolumeFlowProperty(propertyPath);
    }

    private static bool IsOutletOnlyProperty(string propertyPath)
    {
        return propertyPath == "rhoOut" ||
               propertyPath == "adaptiveRhoOutOffset" ||
               propertyPath == "enableMassFluxCorrection" ||
               propertyPath == "targetOutletNormalSpeedPhys" ||
               propertyPath == "outletNormalVelocityBlend" ||
               propertyPath == "outletRhoAnchor" ||
               propertyPath == "maxOutletNormalSpeedPhys" ||
               propertyPath == "forceFullTargetNormalBlendForDebug" ||
               propertyPath == "forceZeroRhoAnchorForDebug";
    }

    private static bool IsVolumeFlowProperty(string propertyPath)
    {
        return propertyPath == "volumeFlowRateUnit" ||
               propertyPath == "volumeFlowRateM3ps" ||
               propertyPath == "volumeFlowRateCMM" ||
               propertyPath == "volumeFlowTangentialAngleAdeg" ||
               propertyPath == "volumeFlowTangentialAngleBdeg" ||
               propertyPath == "volumeFlowDirectionWorld";
    }
}
