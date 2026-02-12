using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UdonSharp;

/// <summary>
/// Validates the Quantum Dharma NPC setup:
/// - All SerializeField component references are assigned
/// - Animator has required parameters (14 float + 8 trigger)
/// - SkinnedMeshRenderer has blend shapes for facial expressions
/// - All required bones exist on the Humanoid avatar
/// Menu: Quantum Dharma > Validate NPC Setup
/// </summary>
public class QuantumDharmaValidator : EditorWindow
{
    private GameObject _npcRoot;
    private Vector2 _scrollPos;
    private readonly List<ValidationEntry> _results = new List<ValidationEntry>();
    private int _errorCount;
    private int _warningCount;
    private int _passCount;

    private enum Severity { Pass, Warning, Error }

    private struct ValidationEntry
    {
        public Severity severity;
        public string message;
    }

    // ── Animator parameters ──
    private static readonly string[] FloatParams = new string[]
    {
        "EmotionCalm", "EmotionCurious", "EmotionWary", "EmotionWarm", "EmotionAfraid",
        "BreathAmplitude", "NpcState", "FreeEnergy", "Trust", "MotorSpeed",
        "MirrorCrouch", "MirrorLean", "GestureIntensity", "Blink"
    };

    private static readonly string[] TriggerParams = new string[]
    {
        "GestureWave", "GestureBow", "GestureHeadTilt", "GestureNod",
        "GestureBeckon", "GestureFlinch", "GestureShake", "GestureRetreat"
    };

    // ── Humanoid bones used by LookAtController and UpperBodyIK ──
    private static readonly HumanBodyBones[] RequiredBones = new HumanBodyBones[]
    {
        HumanBodyBones.Head,
        HumanBodyBones.LeftEye,
        HumanBodyBones.RightEye,
        HumanBodyBones.Spine,
        HumanBodyBones.Chest,
        HumanBodyBones.RightUpperArm,
        HumanBodyBones.LeftUpperArm,
        HumanBodyBones.RightShoulder,
        HumanBodyBones.LeftShoulder
    };

    // ── Fields that are optional / external ──
    private static readonly HashSet<string> OptionalFields = new HashSet<string>
    {
        "CollectiveMemory._peerMemory0",
        "CollectiveMemory._peerMemory1",
        "CollectiveMemory._peerMemory2",
        "CollectiveMemory._peerMemory3",
        "MultiNPCRelay._peer0",
        "MultiNPCRelay._peer1",
        "MultiNPCRelay._peer2",
        "MultiNPCRelay._peer3",
        "PersonalityInstaller._avatarModels",
        "TrustVisualizer._renderers",
        "IdleWaypoints._waypoints"
    };

    [MenuItem("Quantum Dharma/Validate NPC Setup")]
    private static void ShowWindow()
    {
        var window = GetWindow<QuantumDharmaValidator>("QD Validator");
        window.minSize = new Vector2(500, 400);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Quantum Dharma NPC Validator", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Validates component references, Animator parameters, " +
            "blend shapes, and humanoid bones.",
            MessageType.Info);

        EditorGUILayout.Space(4);

        _npcRoot = (GameObject)EditorGUILayout.ObjectField(
            "NPC Root", _npcRoot, typeof(GameObject), true);

        EditorGUILayout.Space(4);

        using (new EditorGUI.DisabledGroupScope(_npcRoot == null))
        {
            if (GUILayout.Button("Run Validation", GUILayout.Height(30)))
            {
                RunValidation();
            }
        }

        if (_results.Count > 0)
        {
            EditorGUILayout.Space(8);

            // Summary
            Color savedColor = GUI.color;
            if (_errorCount > 0)
            {
                GUI.color = new Color(1f, 0.6f, 0.6f);
                EditorGUILayout.LabelField(
                    "ERRORS: " + _errorCount + "  Warnings: " +
                    _warningCount + "  Pass: " + _passCount,
                    EditorStyles.boldLabel);
            }
            else if (_warningCount > 0)
            {
                GUI.color = new Color(1f, 0.9f, 0.5f);
                EditorGUILayout.LabelField(
                    "No errors. Warnings: " + _warningCount +
                    "  Pass: " + _passCount,
                    EditorStyles.boldLabel);
            }
            else
            {
                GUI.color = new Color(0.5f, 1f, 0.5f);
                EditorGUILayout.LabelField(
                    "ALL PASS: " + _passCount + " checks",
                    EditorStyles.boldLabel);
            }
            GUI.color = savedColor;

            // Details
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            foreach (ValidationEntry entry in _results)
            {
                string prefix;
                switch (entry.severity)
                {
                    case Severity.Error:   prefix = "[ERROR] "; break;
                    case Severity.Warning: prefix = "[WARN]  "; break;
                    default:               prefix = "[OK]    "; break;
                }
                EditorGUILayout.LabelField(prefix + entry.message, EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
        }
    }

    private void RunValidation()
    {
        _results.Clear();
        _errorCount = 0;
        _warningCount = 0;
        _passCount = 0;

        if (_npcRoot == null)
        {
            AddResult(Severity.Error, "No NPC root selected.");
            return;
        }

        ValidateComponentReferences();
        ValidateAnimatorParameters();
        ValidateHumanoidBones();
        ValidateBlendShapes();
        ValidateComponentPresence();
    }

    // ── 1. Component Reference Validation ──

    private void ValidateComponentReferences()
    {
        AddResult(Severity.Pass, "=== Component References ===");

        UdonSharpBehaviour[] behaviours =
            _npcRoot.GetComponentsInChildren<UdonSharpBehaviour>(true);

        foreach (UdonSharpBehaviour usb in behaviours)
        {
            Type compType = usb.GetType();
            FieldInfo[] fields = compType.GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            foreach (FieldInfo field in fields)
            {
                if (field.GetCustomAttribute<SerializeField>() == null &&
                    !field.IsPublic)
                {
                    continue;
                }

                Type fieldType = field.FieldType;

                // Only check component references (not primitives, arrays of primitives, etc.)
                bool isComponentRef = typeof(Component).IsAssignableFrom(fieldType);
                bool isComponentArray = fieldType.IsArray &&
                    typeof(Component).IsAssignableFrom(fieldType.GetElementType());
                bool isGameObject = fieldType == typeof(GameObject);
                bool isGameObjectArray = fieldType == typeof(GameObject[]);

                if (!isComponentRef && !isComponentArray &&
                    !isGameObject && !isGameObjectArray)
                {
                    continue;
                }

                string key = compType.Name + "." + field.Name;

                // Check if optional
                bool isOptional = OptionalFields.Contains(key);

                object value = field.GetValue(usb);

                if (isComponentArray || isGameObjectArray)
                {
                    Array arr = value as Array;
                    if (arr == null || arr.Length == 0)
                    {
                        if (isOptional)
                        {
                            AddResult(Severity.Warning,
                                key + " (array) is empty (optional)");
                        }
                        else
                        {
                            AddResult(Severity.Warning,
                                key + " (array) is empty");
                        }
                    }
                    else
                    {
                        AddResult(Severity.Pass, key + " (" + arr.Length + " elements)");
                    }
                }
                else
                {
                    if (value == null || value.Equals(null))
                    {
                        if (isOptional)
                        {
                            AddResult(Severity.Warning,
                                key + " (" + fieldType.Name + ") is null (optional)");
                        }
                        else
                        {
                            AddResult(Severity.Error,
                                key + " (" + fieldType.Name + ") is NULL");
                        }
                    }
                    else
                    {
                        _passCount++;
                    }
                }
            }
        }
    }

    // ── 2. Animator Parameter Validation ──

    private void ValidateAnimatorParameters()
    {
        AddResult(Severity.Pass, "=== Animator Parameters ===");

        Animator animator = _npcRoot.GetComponentInChildren<Animator>(true);
        if (animator == null)
        {
            AddResult(Severity.Error, "No Animator found on NPC hierarchy");
            return;
        }

        RuntimeAnimatorController rac = animator.runtimeAnimatorController;
        if (rac == null)
        {
            AddResult(Severity.Error,
                "Animator has no AnimatorController assigned");
            return;
        }

        // Collect all parameter names
        HashSet<string> existingParams = new HashSet<string>();
        foreach (AnimatorControllerParameter param in animator.parameters)
        {
            existingParams.Add(param.name);
        }

        // Check float parameters
        foreach (string paramName in FloatParams)
        {
            if (existingParams.Contains(paramName))
            {
                AddResult(Severity.Pass, "Float: " + paramName);
            }
            else
            {
                AddResult(Severity.Error,
                    "Missing float parameter: " + paramName);
            }
        }

        // Check trigger parameters
        foreach (string paramName in TriggerParams)
        {
            if (existingParams.Contains(paramName))
            {
                AddResult(Severity.Pass, "Trigger: " + paramName);
            }
            else
            {
                AddResult(Severity.Error,
                    "Missing trigger parameter: " + paramName);
            }
        }
    }

    // ── 3. Humanoid Bone Validation ──

    private void ValidateHumanoidBones()
    {
        AddResult(Severity.Pass, "=== Humanoid Bones ===");

        Animator animator = _npcRoot.GetComponentInChildren<Animator>(true);
        if (animator == null)
        {
            AddResult(Severity.Error, "No Animator found (cannot check bones)");
            return;
        }

        if (!animator.isHuman)
        {
            AddResult(Severity.Error,
                "Animator avatar is not Humanoid (required for IK)");
            return;
        }

        foreach (HumanBodyBones bone in RequiredBones)
        {
            Transform boneTransform = animator.GetBoneTransform(bone);
            if (boneTransform != null)
            {
                AddResult(Severity.Pass, bone.ToString());
            }
            else
            {
                Severity sev = (bone == HumanBodyBones.LeftEye ||
                                bone == HumanBodyBones.RightEye)
                    ? Severity.Warning
                    : Severity.Error;
                AddResult(sev,
                    bone.ToString() + " - bone not mapped" +
                    (sev == Severity.Warning ? " (eye gaze will be disabled)" : ""));
            }
        }
    }

    // ── 4. Blend Shape Validation ──

    private void ValidateBlendShapes()
    {
        AddResult(Severity.Pass, "=== Blend Shapes ===");

        SkinnedMeshRenderer smr =
            _npcRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr == null)
        {
            AddResult(Severity.Warning,
                "No SkinnedMeshRenderer found (facial expressions disabled)");
            return;
        }

        Mesh mesh = smr.sharedMesh;
        if (mesh == null)
        {
            AddResult(Severity.Error, "SkinnedMeshRenderer has no mesh");
            return;
        }

        int count = mesh.blendShapeCount;
        AddResult(Severity.Pass, "Mesh has " + count + " blend shapes");

        if (count == 0)
        {
            AddResult(Severity.Warning,
                "No blend shapes on mesh (facial expressions will not work)");
            return;
        }

        // List all blend shapes for reference
        for (int i = 0; i < count; i++)
        {
            AddResult(Severity.Pass,
                "  [" + i + "] " + mesh.GetBlendShapeName(i));
        }

        // Check FacialExpressionController indices are within range
        UdonSharpBehaviour[] behaviours =
            _npcRoot.GetComponentsInChildren<UdonSharpBehaviour>(true);
        foreach (UdonSharpBehaviour usb in behaviours)
        {
            if (usb.GetType().Name != "FacialExpressionController") continue;

            string[] indexFields = new string[]
            {
                "_blendJoy", "_blendSorrow", "_blendAngry",
                "_blendSurprise", "_blendFear",
                "_blendMouthOpen", "_blendMouthOh"
            };

            FieldInfo[] fields = usb.GetType().GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic);

            foreach (FieldInfo fi in fields)
            {
                bool isBlendIndex = false;
                foreach (string name in indexFields)
                {
                    if (fi.Name == name) { isBlendIndex = true; break; }
                }
                if (!isBlendIndex) continue;

                int idx = (int)fi.GetValue(usb);
                if (idx == -1)
                {
                    AddResult(Severity.Warning,
                        fi.Name + " = -1 (disabled)");
                }
                else if (idx < 0 || idx >= count)
                {
                    AddResult(Severity.Error,
                        fi.Name + " = " + idx +
                        " (out of range, max " + (count - 1) + ")");
                }
                else
                {
                    AddResult(Severity.Pass,
                        fi.Name + " = " + idx +
                        " (" + mesh.GetBlendShapeName(idx) + ")");
                }
            }
            break;
        }
    }

    // ── 5. Component Presence ──

    private void ValidateComponentPresence()
    {
        AddResult(Severity.Pass, "=== Component Presence ===");

        string[] requiredTypes = new string[]
        {
            // Core (29)
            "QuantumDharmaManager", "FreeEnergyCalculator", "BeliefState",
            "QuantumDharmaNPC", "SpeechOrchestrator", "SessionMemory",
            "DreamState", "DreamNarrative", "AdaptivePersonality",
            "CuriosityDrive", "ContextualUtterance", "GroupDynamics",
            "EmotionalContagion", "AttentionSystem", "HabitFormation",
            "MultiNPCRelay", "SharedRitual", "CollectiveMemory",
            "GiftEconomy", "NormFormation", "OralHistory", "NameGiving",
            "Mythology", "CompanionMemory", "FarewellBehavior",
            "PersonalityPreset", "PersonalityInstaller",
            "AutonomousGoals", "EnvironmentAwareness", "ImitationLearning",
            "SensoryGating",
            // Perception (7)
            "PlayerSensor", "MarkovBlanket", "HandProximityDetector",
            "PostureDetector", "TouchSensor", "GiftReceiver", "VoiceDetector",
            // Action (9)
            "NPCMotor", "LookAtController", "EmotionAnimator",
            "MirrorBehavior", "GestureController", "ProximityAudio",
            "IdleWaypoints", "FacialExpressionController", "UpperBodyIK",
            // UI (3)
            "DebugOverlay", "FreeEnergyVisualizer", "TrustVisualizer"
        };

        UdonSharpBehaviour[] behaviours =
            _npcRoot.GetComponentsInChildren<UdonSharpBehaviour>(true);

        HashSet<string> found = new HashSet<string>();
        foreach (UdonSharpBehaviour usb in behaviours)
        {
            found.Add(usb.GetType().Name);
        }

        foreach (string typeName in requiredTypes)
        {
            if (found.Contains(typeName))
            {
                AddResult(Severity.Pass, typeName);
            }
            else
            {
                AddResult(Severity.Error,
                    typeName + " - component not found on hierarchy");
            }
        }
    }

    private void AddResult(Severity severity, string message)
    {
        _results.Add(new ValidationEntry { severity = severity, message = message });
        switch (severity)
        {
            case Severity.Error:   _errorCount++;   break;
            case Severity.Warning: _warningCount++; break;
            case Severity.Pass:    _passCount++;     break;
        }
    }
}
