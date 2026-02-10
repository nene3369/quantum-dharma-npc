using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Creates an AnimatorController with all parameters required by the
/// Quantum Dharma NPC system (14 float + 8 trigger = 22 parameters).
/// Generates a base layer with idle state and an emotion blend tree.
/// Menu: Quantum Dharma > Create Animator Controller
/// </summary>
public class QuantumDharmaAnimatorBuilder : EditorWindow
{
    private string _savePath = "Assets/QuantumDharma/Animations/QuantumDharmaNPC.controller";
    private AnimationClip _idleClip;
    private AnimationClip _walkClip;
    private AnimationClip _calmClip;
    private AnimationClip _curiousClip;
    private AnimationClip _waryClip;
    private AnimationClip _warmClip;
    private AnimationClip _afraidClip;
    private AnimationClip _crouchClip;

    // Gesture clips
    private AnimationClip _waveClip;
    private AnimationClip _bowClip;
    private AnimationClip _headTiltClip;
    private AnimationClip _nodClip;
    private AnimationClip _beckonClip;
    private AnimationClip _flinchClip;
    private AnimationClip _shakeClip;
    private AnimationClip _retreatClip;

    private Vector2 _scrollPos;
    private string _statusMessage = "";

    [MenuItem("Quantum Dharma/Create Animator Controller")]
    private static void ShowWindow()
    {
        var window = GetWindow<QuantumDharmaAnimatorBuilder>("QD Animator Builder");
        window.minSize = new Vector2(450, 600);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Quantum Dharma Animator Builder", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Creates an AnimatorController with 22 parameters " +
            "(14 float + 8 trigger), emotion blend tree, locomotion layer, " +
            "and gesture layer. Assign animation clips below (optional).",
            MessageType.Info);

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        EditorGUILayout.Space(4);
        _savePath = EditorGUILayout.TextField("Save Path", _savePath);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Base Clips", EditorStyles.boldLabel);
        _idleClip = ClipField("Idle", _idleClip);
        _walkClip = ClipField("Walk / Locomotion", _walkClip);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Emotion Clips (Blend Tree)", EditorStyles.boldLabel);
        _calmClip    = ClipField("Calm", _calmClip);
        _curiousClip = ClipField("Curious", _curiousClip);
        _waryClip    = ClipField("Wary", _waryClip);
        _warmClip    = ClipField("Warm", _warmClip);
        _afraidClip  = ClipField("Afraid", _afraidClip);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Mirror Clips", EditorStyles.boldLabel);
        _crouchClip = ClipField("Crouch / Mirror", _crouchClip);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Gesture Clips", EditorStyles.boldLabel);
        _waveClip     = ClipField("Wave", _waveClip);
        _bowClip      = ClipField("Bow", _bowClip);
        _headTiltClip = ClipField("Head Tilt", _headTiltClip);
        _nodClip      = ClipField("Nod", _nodClip);
        _beckonClip   = ClipField("Beckon", _beckonClip);
        _flinchClip   = ClipField("Flinch", _flinchClip);
        _shakeClip    = ClipField("Shake", _shakeClip);
        _retreatClip  = ClipField("Retreat Step", _retreatClip);

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(8);

        if (GUILayout.Button("Generate Animator Controller", GUILayout.Height(35)))
        {
            GenerateController();
        }

        if (!string.IsNullOrEmpty(_statusMessage))
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);
        }
    }

    private AnimationClip ClipField(string label, AnimationClip clip)
    {
        return (AnimationClip)EditorGUILayout.ObjectField(
            label, clip, typeof(AnimationClip), false);
    }

    private void GenerateController()
    {
        // Ensure directory exists
        string directory = System.IO.Path.GetDirectoryName(_savePath);
        if (!string.IsNullOrEmpty(directory) && !AssetDatabase.IsValidFolder(directory))
        {
            string[] parts = directory.Replace("\\", "/").Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        AnimatorController controller =
            AnimatorController.CreateAnimatorControllerAtPath(_savePath);

        // ── Add Float Parameters (14) ──
        string[] floatParams = new string[]
        {
            "EmotionCalm", "EmotionCurious", "EmotionWary",
            "EmotionWarm", "EmotionAfraid",
            "BreathAmplitude", "NpcState", "FreeEnergy",
            "Trust", "MotorSpeed",
            "MirrorCrouch", "MirrorLean",
            "GestureIntensity", "Blink"
        };

        foreach (string param in floatParams)
        {
            controller.AddParameter(param, AnimatorControllerParameterType.Float);
        }

        // ── Add Trigger Parameters (8) ──
        string[] triggerParams = new string[]
        {
            "GestureWave", "GestureBow", "GestureHeadTilt", "GestureNod",
            "GestureBeckon", "GestureFlinch", "GestureShake", "GestureRetreat"
        };

        foreach (string param in triggerParams)
        {
            controller.AddParameter(param, AnimatorControllerParameterType.Trigger);
        }

        // ── Layer 0: Base (Locomotion + Idle) ──
        AnimatorControllerLayer baseLayer = controller.layers[0];
        AnimatorStateMachine baseSM = baseLayer.stateMachine;
        baseSM.name = "Base";

        // Idle state (default)
        AnimatorState idleState = baseSM.AddState("Idle", new Vector3(300, 0, 0));
        idleState.motion = _idleClip;
        baseSM.defaultState = idleState;

        // Walk state driven by MotorSpeed
        AnimatorState walkState = baseSM.AddState("Walk", new Vector3(300, 80, 0));
        walkState.motion = _walkClip;
        walkState.speedParameterActive = true;
        walkState.speedParameter = "MotorSpeed";

        // Idle -> Walk when MotorSpeed > 0.1
        AnimatorStateTransition toWalk = idleState.AddTransition(walkState);
        toWalk.AddCondition(AnimatorConditionMode.Greater, 0.1f, "MotorSpeed");
        toWalk.hasExitTime = false;
        toWalk.duration = 0.2f;

        // Walk -> Idle when MotorSpeed < 0.05
        AnimatorStateTransition toIdle = walkState.AddTransition(idleState);
        toIdle.AddCondition(AnimatorConditionMode.Less, 0.05f, "MotorSpeed");
        toIdle.hasExitTime = false;
        toIdle.duration = 0.2f;

        // ── Layer 1: Emotion (Blend Tree, Additive) ──
        controller.AddLayer("Emotion");
        AnimatorControllerLayer[] layers = controller.layers;
        AnimatorControllerLayer emotionLayer = layers[1];
        emotionLayer.blendingMode = AnimatorLayerBlendingMode.Additive;
        emotionLayer.defaultWeight = 1f;
        controller.layers = layers;

        AnimatorStateMachine emotionSM = emotionLayer.stateMachine;

        // Create a 2D Freeform blend tree with emotion parameters
        BlendTree emotionTree;
        AnimatorState emotionState = controller.CreateBlendTreeInController(
            "EmotionBlend", out emotionTree, 1);
        emotionState.writeDefaultValues = true;

        emotionTree.blendType = BlendTreeType.FreeformDirectional2D;
        emotionTree.blendParameter = "EmotionCalm";
        emotionTree.blendParameterY = "EmotionWary";

        // Add motions (calm at center, others on axes)
        AnimationClip placeholder = _calmClip;
        emotionTree.AddChild(placeholder != null ? placeholder : _idleClip, new Vector2(1f, 0f));  // Calm
        emotionTree.AddChild(_curiousClip != null ? _curiousClip : _idleClip, new Vector2(0.5f, -0.5f)); // Curious
        emotionTree.AddChild(_waryClip != null ? _waryClip : _idleClip, new Vector2(0f, 1f));      // Wary
        emotionTree.AddChild(_warmClip != null ? _warmClip : _idleClip, new Vector2(0.5f, 0.5f));  // Warm
        emotionTree.AddChild(_afraidClip != null ? _afraidClip : _idleClip, new Vector2(-0.5f, 1f)); // Afraid

        emotionSM.defaultState = emotionState;

        // ── Layer 2: Mirror (Override, weight 0 by default) ──
        controller.AddLayer("Mirror");
        layers = controller.layers;
        AnimatorControllerLayer mirrorLayer = layers[2];
        mirrorLayer.blendingMode = AnimatorLayerBlendingMode.Override;
        mirrorLayer.defaultWeight = 0f;
        controller.layers = layers;

        AnimatorStateMachine mirrorSM = mirrorLayer.stateMachine;
        AnimatorState mirrorIdle = mirrorSM.AddState("MirrorIdle", new Vector3(300, 0, 0));
        mirrorSM.defaultState = mirrorIdle;

        if (_crouchClip != null)
        {
            AnimatorState crouchState = mirrorSM.AddState("Crouch", new Vector3(300, 80, 0));
            crouchState.motion = _crouchClip;

            AnimatorStateTransition toCrouch = mirrorIdle.AddTransition(crouchState);
            toCrouch.AddCondition(AnimatorConditionMode.Greater, 0.1f, "MirrorCrouch");
            toCrouch.hasExitTime = false;
            toCrouch.duration = 0.3f;

            AnimatorStateTransition fromCrouch = crouchState.AddTransition(mirrorIdle);
            fromCrouch.AddCondition(AnimatorConditionMode.Less, 0.05f, "MirrorCrouch");
            fromCrouch.hasExitTime = false;
            fromCrouch.duration = 0.3f;
        }

        // ── Layer 3: Gesture (Override, AvatarMask upper body recommended) ──
        controller.AddLayer("Gesture");
        layers = controller.layers;
        AnimatorControllerLayer gestureLayer = layers[3];
        gestureLayer.blendingMode = AnimatorLayerBlendingMode.Override;
        gestureLayer.defaultWeight = 1f;
        controller.layers = layers;

        AnimatorStateMachine gestureSM = gestureLayer.stateMachine;
        AnimatorState gestureIdle = gestureSM.AddState("GestureIdle", new Vector3(300, 0, 0));
        gestureSM.defaultState = gestureIdle;

        // Create gesture states with trigger transitions
        AnimationClip[] gestureClips = new AnimationClip[]
        {
            _waveClip, _bowClip, _headTiltClip, _nodClip,
            _beckonClip, _flinchClip, _shakeClip, _retreatClip
        };

        string[] gestureNames = new string[]
        {
            "Wave", "Bow", "HeadTilt", "Nod",
            "Beckon", "Flinch", "Shake", "Retreat"
        };

        for (int i = 0; i < gestureNames.Length; i++)
        {
            AnimatorState gState = gestureSM.AddState(
                gestureNames[i], new Vector3(550, i * 60, 0));
            gState.motion = gestureClips[i];

            // Trigger -> gesture state
            AnimatorStateTransition toGesture = gestureIdle.AddTransition(gState);
            toGesture.AddCondition(
                AnimatorConditionMode.If, 0f, "Gesture" + gestureNames[i]);
            toGesture.hasExitTime = false;
            toGesture.duration = 0.1f;

            // Gesture state -> idle (exit time)
            AnimatorStateTransition backToIdle = gState.AddTransition(gestureIdle);
            backToIdle.hasExitTime = true;
            backToIdle.exitTime = 0.9f;
            backToIdle.duration = 0.2f;
        }

        // Save
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        _statusMessage =
            "AnimatorController created at: " + _savePath +
            "\n22 parameters (14 float + 8 trigger)" +
            "\n4 layers: Base, Emotion, Mirror, Gesture";

        Debug.Log("[QuantumDharma] AnimatorController generated: " + _savePath);
    }
}
