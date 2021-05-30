using System;
using System.IO;
using System.Text;
using UnityEngine;

public class GestureRecording
{
    public string instruction = "";

    public string sceneType = "Kitchen";
    public int sceneNum = 0;
    public string sceneName = "FloorPlan1_physics";

    public string targetType = "";
    public string targetSimObjType = "";
    public string targetID = "";

    public Vector3 humanPos = Vector3.zero;
    public float humanRot =  0f;
    public Vector3 targetPos = Vector3.zero;
    public Vector3 targetToHuman = Vector3.zero;

    public string image = "images/";
    public string motion = "motions/";
    public string audio = "audios/";
}

