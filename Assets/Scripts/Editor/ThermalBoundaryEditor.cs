using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DeviceObstacles))]
[CanEditMultipleObjects]
public class ThermalBoundaryEditor : Editor
{
    SerializedProperty _boundaryTypeProp;
    SerializedProperty _temperatureProp;

    private void OnEnable()
    {
        _boundaryTypeProp = serializedObject.FindProperty("boundaryType");
        _temperatureProp = serializedObject.FindProperty("temperature");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(_boundaryTypeProp);

        bool isIsothermal = _boundaryTypeProp.enumValueIndex == (int)ThermalBoundaryType.Isothermal;

        using (new EditorGUI.DisabledScope(!isIsothermal))
        {
            EditorGUILayout.PropertyField(_temperatureProp);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
