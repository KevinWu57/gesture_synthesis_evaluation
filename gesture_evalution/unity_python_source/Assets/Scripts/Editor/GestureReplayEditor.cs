using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(GestureReplay))]
public class GestureReplayEditor : Editor
{
    public override void OnInspectorGUI()
    {
        GestureReplay mScript = (GestureReplay)target;

        mScript.sequence_length = EditorGUILayout.IntField("Gesture sequence length", mScript.sequence_length);

        mScript.target_scale = EditorGUILayout.FloatField("Dummy target scale", mScript.target_scale);

        mScript.metadata_path = EditorGUILayout.TextField("Loaded metadata file path", mScript.metadata_path);
        mScript.motion_path = EditorGUILayout.TextField("Loaded motion file path", mScript.motion_path);
    
        if(GUILayout.Button("Play Gesture from CSV File"))
        {
            if (mScript.LoadAnimation(mScript.motion_path)) mScript.reapplyPoses = true;
        }
    }
}
