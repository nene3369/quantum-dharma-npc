using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Validates that an AnimatorController has all required states and
/// animation clips assigned for Quantum Dharma NPC operation.
///
/// Checks for:
///   - Required base layer states (Idle, Walk, Run)
///   - Required emotion blend tree parameters
///   - Required gesture states
///   - Empty/missing clip assignments
///   - Parameter count (22 expected)
///
/// Menu: Quantum Dharma > Validate Animation Clips
/// </summary>
public class QuantumDharmaAnimationClipValidator : EditorWindow
{
    private Animator _animator;
    private Vector2 _scrollPos;
    private readonly List<ValidationResult> _results = new List<ValidationResult>();

    private struct ValidationResult
    {
        public string category;
        public string message;
        public int severity; // 0=OK, 1=WARN, 2=ERROR
    }

    // Required animator parameters (22 total)
    // Must match QuantumDharmaAnimatorBuilder output exactly
    private static readonly string[] RequiredParams = new string[]
    {
        // Float parameters (14)
        "EmotionCalm", "EmotionCurious", "EmotionWary", "EmotionWarm", "EmotionAfraid",
        "BreathAmplitude", "NpcState", "FreeEnergy", "Trust", "MotorSpeed",
        "MirrorCrouch", "MirrorLean", "GestureIntensity", "Blink",
        // Trigger parameters (8)
        "GestureWave", "GestureBow", "GestureHeadTilt", "GestureNod",
        "GestureBeckon", "GestureFlinch", "GestureShake", "GestureRetreat"
    };

    // Required base layer states
    private static readonly string[] RequiredBaseStates = new string[]
    {
        "Idle", "Walk"
    };

    // Required layers
    private static readonly string[] RequiredLayers = new string[]
    {
        "Base Layer", "Emotion", "Mirror", "Gesture"
    };

    [MenuItem("Quantum Dharma/Validate Animation Clips")]
    private static void ShowWindow()
    {
        GetWindow<QuantumDharmaAnimationClipValidator>("Animation Validator");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Animation Clip Validator", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Select an Animator component to validate that all required states, "
            + "parameters, and clips are properly configured.",
            MessageType.Info);
        EditorGUILayout.Space();

        _animator = (Animator)EditorGUILayout.ObjectField("Animator", _animator, typeof(Animator), true);

        EditorGUILayout.Space();

        EditorGUI.BeginDisabledGroup(_animator == null);
        if (GUILayout.Button("Validate", GUILayout.Height(25)))
        {
            _results.Clear();
            Validate();
        }
        EditorGUI.EndDisabledGroup();

        // Results
        if (_results.Count > 0)
        {
            EditorGUILayout.Space();
            int errors = 0, warnings = 0, ok = 0;
            foreach (ValidationResult r in _results)
            {
                if (r.severity == 2) errors++;
                else if (r.severity == 1) warnings++;
                else ok++;
            }
            EditorGUILayout.LabelField(
                "Results: " + ok + " OK, " + warnings + " warnings, " + errors + " errors",
                EditorStyles.boldLabel);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(300));
            foreach (ValidationResult r in _results)
            {
                Color oldColor = GUI.color;
                if (r.severity == 2) GUI.color = new Color(1f, 0.4f, 0.4f);
                else if (r.severity == 1) GUI.color = Color.yellow;
                else GUI.color = Color.green;

                string prefix = r.severity == 2 ? "ERROR" : r.severity == 1 ? "WARN" : "OK";
                EditorGUILayout.LabelField("[" + prefix + "] " + r.category + ": " + r.message,
                    EditorStyles.wordWrappedLabel);
                GUI.color = oldColor;
            }
            EditorGUILayout.EndScrollView();
        }
    }

    private void Validate()
    {
        if (_animator == null) return;

        AnimatorController controller = _animator.runtimeAnimatorController as AnimatorController;
        if (controller == null)
        {
            AddResult("Controller", "No AnimatorController assigned", 2);
            return;
        }

        // Validate parameters
        ValidateParameters(controller);

        // Validate layers
        ValidateLayers(controller);

        // Validate states and clips
        ValidateStatesAndClips(controller);

        // Validate humanoid rig
        ValidateHumanoidRig();
    }

    private void ValidateParameters(AnimatorController controller)
    {
        AnimatorControllerParameter[] existing = controller.parameters;
        HashSet<string> existingNames = new HashSet<string>();
        foreach (AnimatorControllerParameter p in existing)
        {
            existingNames.Add(p.name);
        }

        int found = 0;
        foreach (string reqParam in RequiredParams)
        {
            if (existingNames.Contains(reqParam))
            {
                found++;
            }
            else
            {
                AddResult("Parameter", "Missing: " + reqParam, 2);
            }
        }

        if (found == RequiredParams.Length)
        {
            AddResult("Parameters", "All " + RequiredParams.Length + " parameters present", 0);
        }
        else
        {
            AddResult("Parameters", found + "/" + RequiredParams.Length + " present", 1);
        }
    }

    private void ValidateLayers(AnimatorController controller)
    {
        AnimatorControllerLayer[] layers = controller.layers;
        HashSet<string> layerNames = new HashSet<string>();
        foreach (AnimatorControllerLayer l in layers)
        {
            layerNames.Add(l.name);
        }

        foreach (string reqLayer in RequiredLayers)
        {
            if (layerNames.Contains(reqLayer))
            {
                AddResult("Layer", reqLayer + " present", 0);
            }
            else
            {
                AddResult("Layer", "Missing: " + reqLayer, 2);
            }
        }
    }

    private void ValidateStatesAndClips(AnimatorController controller)
    {
        if (controller.layers.Length == 0) return;

        // Check base layer states
        AnimatorStateMachine baseSM = controller.layers[0].stateMachine;
        HashSet<string> stateNames = new HashSet<string>();
        foreach (ChildAnimatorState cs in baseSM.states)
        {
            stateNames.Add(cs.state.name);

            // Check for empty clips
            if (cs.state.motion == null)
            {
                AddResult("Clip", cs.state.name + " has no motion assigned", 1);
            }
        }

        foreach (string reqState in RequiredBaseStates)
        {
            if (stateNames.Contains(reqState))
            {
                AddResult("State", reqState + " present in Base Layer", 0);
            }
            else
            {
                AddResult("State", "Missing: " + reqState + " in Base Layer", 2);
            }
        }

        // Check for empty blend trees in emotion layer
        if (controller.layers.Length > 1)
        {
            AnimatorStateMachine emotionSM = controller.layers[1].stateMachine;
            foreach (ChildAnimatorState cs in emotionSM.states)
            {
                if (cs.state.motion == null)
                {
                    AddResult("Emotion", cs.state.name + " has no motion", 1);
                }
            }
        }
    }

    private void ValidateHumanoidRig()
    {
        if (!_animator.isHuman)
        {
            AddResult("Rig", "Animator is not humanoid — IK features will not work", 2);
            return;
        }
        AddResult("Rig", "Humanoid rig detected", 0);

        // Check essential bones
        string[] essentialBones = { "Head", "Spine", "LeftHand", "RightHand" };
        foreach (string boneName in essentialBones)
        {
            HumanBodyBones bone;
            try
            {
                bone = (HumanBodyBones)System.Enum.Parse(typeof(HumanBodyBones), boneName);
            }
            catch
            {
                continue;
            }
            Transform boneTransform = _animator.GetBoneTransform(bone);
            if (boneTransform != null)
            {
                AddResult("Bone", boneName + " mapped", 0);
            }
            else
            {
                AddResult("Bone", boneName + " NOT mapped", 1);
            }
        }
    }

    private void AddResult(string category, string message, int severity)
    {
        ValidationResult r = new ValidationResult();
        r.category = category;
        r.message = message;
        r.severity = severity;
        _results.Add(r);
    }
}
