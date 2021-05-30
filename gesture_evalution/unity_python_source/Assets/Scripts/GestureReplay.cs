using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Net;
using System.Net.Sockets;
using System.Threading;

public class GestureReplay : MonoBehaviour
{
    public bool reapplyPoses; // If this is set to true, human avatar will try to read from the loaded animation file and apply the motions
    public int sequence_length = 100; // Total sequence length
    public float target_scale = 1.0f;
    private HumanPose poseToSet; // reassemble poses from .csv data
    private HumanPoseHandler poseHandler; // to record and retarget animation
    private int muscleCount; // the number of muscles in current setting
    private float[] currentMuscles; // an array containig current muscle values
    private float[,] animationHumanPoses; // stack all currentHumanPose in one array
    private int counterPlay = 0; // count animation playback frames
    private int counterLoad = 0; // count number of frames of loaded animation
    private Vector3 defaultPos;
    private Quaternion defaultRot;
    private Vector3 defaultBodyPos;
    private Quaternion defaultBodyRot;
    private float[] defaultMuscles;
    private Vector3 initialPos; // initial position and rotation for each motion
    private Quaternion initialRot;
    private Animator animator;

    [HideInInspector] public bool isTxStarted = false;
    public string IP = "127.0.0.1"; // local host
    public int rxPort = 8000; // port to receive data from Python on
    public int txPort = 8001; // port to send data to Python on

    // Create necessary UdpClient objects
    UdpClient client;
    IPEndPoint remoteEndPoint;
    Thread receiveThread; // Receiving Thread

    GameObject targetObj;
    Transform targetT;
    GestureRecording recording;
    float[] muscles; // Muscle values at the current frame
    int validFrame = 0;
    bool readyToSend = false; // Send message to python only when this is set to true

    public string metadata_path = "";
    public string motion_path = "";

    // UI components
    private InputField metadataPathInputField;
    private InputField motionPathInputField;
    private InputField msgSend2PythonInputField;
    private Text resultsText;

    // Python replay control. Set it true to display all the information on canvas
    private bool playFromPython = false;

    // Error display control
    private bool displayErrMsg = false;
    private string errMsg = "";

    void Awake()
    {
        muscles = new float[HumanTrait.MuscleCount];

        // Create remote endpoint (to Matlab) 
        remoteEndPoint = new IPEndPoint(IPAddress.Parse(IP), txPort);

        // Create local client
        client = new UdpClient(rxPort);

        // local endpoint define (where messages are received)
        // Create a new thread for reception of incoming messages
        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();

        // Initialize (seen in comments window)
        print("UDP Comms Initialised");

        // gestureReplay = FindObjectOfType<GestureReplay>();

        // Spawn a target sphere at the default location
        targetObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        targetObj.layer = 9;
        targetT = targetObj.transform;
        targetObj.transform.rotation = Quaternion.identity;
        targetObj.transform.localScale = Vector3.one*target_scale;
        targetObj.AddComponent<BoxCollider>();

        // UI components
        metadataPathInputField = GameObject.Find("MetadataPath").GetComponent<InputField>(); 
        motionPathInputField = GameObject.Find("MotionPath").GetComponent<InputField>(); 
        msgSend2PythonInputField = GameObject.Find("MsgSend2Python").GetComponent<InputField>();
        resultsText = GameObject.Find("Results").GetComponent<Text>();
    }

    private void Start() 
    {
        animator = GetComponent<Animator>();
        poseHandler = new HumanPoseHandler(animator.avatar, transform);

        muscleCount = HumanTrait.MuscleCount; // count the number of muscles of the avatar
        currentMuscles = new float[muscleCount]; 

        // Get all the default information
        defaultPos = transform.position;
        defaultRot = transform.rotation;
        poseHandler.GetHumanPose(ref poseToSet);
        defaultBodyPos = poseToSet.bodyPosition;
        defaultBodyRot = poseToSet.bodyRotation;
        defaultMuscles = poseToSet.muscles;

        initialPos = poseToSet.bodyPosition;
        initialRot = poseToSet.bodyRotation;
    }

    void FixedUpdate()
    {
        if (reapplyPoses) 
        {
            if (playFromPython)
            {
                metadataPathInputField.text = metadata_path;
                motionPathInputField.text = motion_path;
                playFromPython = false;
            }
            reapplyPosesAnimation();
        }

        if (displayErrMsg)
        {
            DisplayErrMsg(resultsText, errMsg);
            displayErrMsg = false;
        }
    }

    private void DisplayErrMsg(Text text, string errMsg)
    {
        text.color = Color.red;
        text.text = errMsg;
    }

    /// <summary>
    /// Send a specific message to python
    /// </summary>
    /// <param name="message"></param>
    public void SendData(string message) // Use to send data to Python
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            client.Send(data, data.Length, remoteEndPoint);
        }
        catch (Exception err)
        {
            Debug.LogError(err.ToString());
            displayErrMsg = true;
            errMsg = err.ToString();
        }
    }

    /// <summary>
    /// Send a message in the input field to python
    /// </summary>
    public void SentData()
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(msgSend2PythonInputField.text);
            client.Send(data, data.Length, remoteEndPoint);
            print(String.Format("You have sent '{0}' to Python.", msgSend2PythonInputField.text));
        }
        catch (Exception err)
        {
            Debug.LogError(err.ToString());
            displayErrMsg = true;
            errMsg = err.ToString();
        }
    }

    // Receive data, update packets received
    private void ReceiveData()
    {
        while (true)
        {
            try
            {
                IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = client.Receive(ref anyIP);
                string[] paths = Encoding.UTF8.GetString(data).Split(' '); // metadata_path motion_path
                metadata_path = paths[0];
                motion_path = paths[1];
                if (metadata_path != null && motion_path != null)
                {
                    print(">> Read experiment metadata: " + metadata_path);
                    print(">> Read experiment motion: " + motion_path);

                    ReadFiles(metadata_path, motion_path);

                    playFromPython = true;
                }
            }
            catch (Exception err)
            {
                Debug.LogError(err.ToString());
                displayErrMsg = true;
                errMsg = err.ToString();
            }
        }
    }

    private void ReadFiles(string metadata_path, string motion_path)
    {
        // Read the json file
        string path_json = metadata_path.EndsWith(".json")? metadata_path:(metadata_path+".json"); // auto-correct for the filename suffix
        try
        {
            string json = File.ReadAllText(path_json);
            recording = JsonUtility.FromJson<GestureRecording>(json);

            if (LoadAnimation(motion_path)) {reapplyPoses = true;}
        }
        catch (Exception err)
        {
            Debug.LogError(err.ToString());
            displayErrMsg = true;
            errMsg = err.ToString();
        }
    }

    public void ReadFiles()
    {
        // Read the json file
        string path_json = metadataPathInputField.text.EndsWith(".json")? metadataPathInputField.text:(metadataPathInputField.text+".json"); // auto-correct for the filename suffix
        try
        {
            string json = File.ReadAllText(path_json);
            recording = JsonUtility.FromJson<GestureRecording>(json);

            if (LoadAnimation(motionPathInputField.text)) {reapplyPoses = true;}
        }
        catch (Exception err)
        {
            Debug.LogError(err.ToString());
            displayErrMsg = true;
            errMsg = err.ToString();
        }
    }

    // Only load the animation for one frame
    public bool LoadAnimationOneFrame(float[] muscleValues, GameObject targetObj)
    {
        // Set the transform
        transform.position = Vector3.zero;
        // Local position
        Vector3 localPos = transform.InverseTransformPoint(initialPos);

        poseToSet.bodyPosition = localPos;
        poseToSet.bodyRotation = Quaternion.identity;

        // // int currentFrame = counterPlay%counterLoad;   
        
        poseToSet.muscles = muscleValues;
        poseHandler.SetHumanPose(ref poseToSet);

        // Check if the current pose is refering to the target
        return CheckPoseReferToTarget(targetObj);
    }

     // Refill animationHumanPoses with values from loaded csv files
    public bool LoadAnimation(string loadedFile)
    {
        animationHumanPoses = new float[sequence_length, muscleCount];
        
        // Disable body controller
        // humanMocapAnimator.GetComponent<Animator>().runtimeAnimatorController = null;

        string path = loadedFile.EndsWith(".csv")? loadedFile:(loadedFile+".csv"); // auto-correct for the filename suffix

        try
        {

            StreamReader sr = new StreamReader(path);
            int frame = 0;
            string[] line;
            while (!sr.EndOfStream)
            {
                line = sr.ReadLine().Split(';');
                if(frame != 0)
                {
                    for (int muscleNum = 0; muscleNum < line.Length - 1; muscleNum++)
                    {
                        animationHumanPoses[frame-1, muscleNum] = float.Parse(line[muscleNum]);
                    }
                }
                frame++;
            }
            counterLoad = frame-1;
            return true;
        }
        catch
        {
            Debug.LogError($"Motion file at path '{path}' is not found. Please specify a correct path.");
            displayErrMsg = true;
            errMsg = $"Motion file at path '{path}' is not found. Please specify a correct path.";
            return false;
        }

    }


    /// <summary>
    /// This function is used to target if the human pose at the current frame is pointing to the target
    /// We consider the frame as valid (i.e. pointing to the target) if any of the frames emitted from the <see cref="HumanBonesCheckList"/>
    /// hits the dummy target sphere
    /// </summary>
    /// <param name="targetObj"></param>
    /// <returns></returns>
    public bool CheckPoseReferToTarget(GameObject targetObj)
    {
        foreach(HumanBodyBones bone in HumanBonesCheckList)
        {
            Transform boneT = animator.GetBoneTransform(bone);
            #if UNITY_EDITOR
            Debug.DrawRay(boneT.position, (bone == HumanBodyBones.Head? boneT.forward:boneT.right)*10f, Color.red, 0.0f);
            #endif
            // Make a raycast to see if it could collide with object
            if (Physics.Raycast(boneT.position, (bone == HumanBodyBones.Head? boneT.forward:boneT.right))) return true;
        }
        return false;
    }

    // Loop through array and apply poses one frame after another. 
    public void reapplyPosesAnimation()
    {
        if(counterPlay==0)
        {
            // Reset the human pose
            poseToSet.bodyPosition = new Vector3(0f, initialPos.y, 0f);
            poseToSet.bodyRotation = Quaternion.identity;
            poseHandler.SetHumanPose(ref poseToSet);

            if (recording != null)
            {
                // Move the human avatar and target to the designated pose
                targetT.position = recording.targetToHuman *10f;
                // transform.position = recording.humanPos * 10f;
                transform.rotation = Quaternion.Euler(0f, recording.humanRot*360f, 0f);
            }

            // The following steps are necessary for the avater to correctly replay motion.
            #region Position Avatar to the Designated Position
            poseHandler.GetHumanPose(ref poseToSet);
            initialPos = poseToSet.bodyPosition;
            initialRot = poseToSet.bodyRotation;

            // Set the transform. It is so weird for the mechanim system, so strange
            transform.position = Vector3.zero;
            // Local position
            Vector3 localPos = transform.InverseTransformPoint(initialPos);
            poseToSet.bodyPosition = localPos;
            poseToSet.bodyRotation = Quaternion.identity;
            #endregion
        }
        
        for (int i = 0; i < muscleCount; i++) { currentMuscles[i] = animationHumanPoses[counterPlay, i]; } // somehow cannot directly modify muscle values
        poseToSet.muscles = currentMuscles;
        poseHandler.SetHumanPose(ref poseToSet);

        // Check if the current frame is a valid frame. If so, increase the counter.
        if (CheckPoseReferToTarget(targetObj)) validFrame++;

        counterPlay++;

        // Stop reapplying poses
        if(counterPlay == counterLoad)
        {
            counterPlay = 0;
            counterLoad = 0;
            reapplyPoses = false;
            readyToSend = true;

            // Reset human avatar poses
            ResetPoses();

            if (readyToSend)
            {
                // Send the accuracy of the current gesture to python
                string gestAcc = (validFrame*1f/sequence_length).ToString();
                SendData(gestAcc);

                // Display the accuracy on the screen
                resultsText.text = $"The accuracy for this gesture is: <b>{gestAcc}</b>";

                validFrame = 0;
                recording = null;
                readyToSend = false;
            }
        }              
    }

    private void ResetPoses()
    {
        transform.position = defaultPos;
        transform.rotation = defaultRot;
        poseToSet.bodyPosition = defaultBodyPos;
        poseToSet.bodyRotation = defaultBodyRot;
        poseToSet.muscles = defaultMuscles;
        poseHandler.SetHumanPose(ref poseToSet);
    }

    // Quit the application
    public void QuitApplication()
    {
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }

    private static HumanBodyBones[] HumanBonesCheckList = new HumanBodyBones[]{
        HumanBodyBones.Head,
        HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
        HumanBodyBones.LeftThumbDistal, HumanBodyBones.RightThumbDistal,
        HumanBodyBones.LeftIndexDistal, HumanBodyBones.RightIndexDistal,
        HumanBodyBones.LeftMiddleDistal, HumanBodyBones.RightMiddleDistal,
        HumanBodyBones.LeftRingDistal, HumanBodyBones.RightRingDistal,
        HumanBodyBones.LeftLittleDistal, HumanBodyBones.RightLittleDistal,
    };

    //Prevent crashes - close clients and threads properly!
    void OnDisable()
    {
        if (receiveThread != null)
            receiveThread.Abort();

        client.Close();
    }
}