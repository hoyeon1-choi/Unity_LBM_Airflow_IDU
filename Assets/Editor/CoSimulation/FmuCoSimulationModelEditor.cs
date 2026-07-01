using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(FmuCoSimulationModel))]
public class FmuCoSimulationModelEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        FmuCoSimulationModel model = (FmuCoSimulationModel)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("FMU Parameter Tools", EditorStyles.boldLabel);

        if (GUILayout.Button("Load Missing Real Parameters From FMU"))
        {
            Undo.RecordObject(model, "Load FMU Real Parameters");
            model.PopulateRealParameterOverridesFromModelDescription(false);
            EditorUtility.SetDirty(model);
        }

        if (GUILayout.Button("Reset Real Parameters To FMU Defaults"))
        {
            Undo.RecordObject(model, "Reset FMU Real Parameters");
            model.PopulateRealParameterOverridesFromModelDescription(true);
            EditorUtility.SetDirty(model);
        }

        if (GUILayout.Button("Apply Real Parameters Now"))
        {
            model.ApplyRealParametersFromInspector();
            EditorUtility.SetDirty(model);
        }
    }
}
