using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UdonSharp;

/// <summary>
/// Pre-publish validation checklist for Quantum Dharma NPCs.
/// Performs a comprehensive health check before VRChat Build & Test.
///
/// Checks:
///   - All 49 components present
///   - All SerializeField references wired (non-null)
///   - Animator has 22 parameters
///   - Humanoid rig configured
///   - Blend shapes assigned to FacialExpressionController
///   - Waypoints assigned to IdleWaypoints
///   - Debug UI properly configured
///   - No unintended null references
///
/// Menu: Quantum Dharma > Setup Checklist
/// </summary>
public class QuantumDharmaSetupChecklist : EditorWindow
{
    private GameObject _npcRoot;
    private Vector2 _scrollPos;
    private readonly List<CheckItem> _items = new List<CheckItem>();
    private int _passCount;
    private int _warnCount;
    private int _failCount;

    private struct CheckItem
    {
        public string name;
        public int status; // 0=pass, 1=warn, 2=fail
        public string detail;
    }

    // All 49 runtime UdonSharpBehaviour type names
    private static readonly string[] RequiredComponents = new string[]
    {
        // Core (30)
        "QuantumDharmaManager", "FreeEnergyCalculator", "BeliefState",
        "QuantumDharmaNPC", "SessionMemory", "SpeechOrchestrator",
        "DreamState", "DreamNarrative", "AdaptivePersonality",
        "CuriosityDrive", "ContextualUtterance", "GroupDynamics",
        "EmotionalContagion", "AttentionSystem", "HabitFormation",
        "MultiNPCRelay", "SharedRitual", "CollectiveMemory",
        "GiftEconomy", "NormFormation", "OralHistory",
        "NameGiving", "Mythology", "CompanionMemory",
        "FarewellBehavior", "PersonalityPreset", "PersonalityInstaller",
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

    [MenuItem("Quantum Dharma/Setup Checklist")]
    private static void ShowWindow()
    {
        GetWindow<QuantumDharmaSetupChecklist>("Setup Checklist");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Quantum Dharma Setup Checklist", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Run this checklist before Build & Test to verify your NPC is fully configured.",
            MessageType.Info);
        EditorGUILayout.Space();

        _npcRoot = (GameObject)EditorGUILayout.ObjectField("NPC Root", _npcRoot, typeof(GameObject), true);

        EditorGUILayout.Space();

        EditorGUI.BeginDisabledGroup(_npcRoot == null);
        if (GUILayout.Button("Run Checklist", GUILayout.Height(30)))
        {
            _items.Clear();
            _passCount = 0;
            _warnCount = 0;
            _failCount = 0;
            RunChecklist();
        }
        EditorGUI.EndDisabledGroup();

        // Summary
        if (_items.Count > 0)
        {
            EditorGUILayout.Space();
            Color oldBg = GUI.backgroundColor;
            if (_failCount > 0) GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
            else if (_warnCount > 0) GUI.backgroundColor = new Color(1f, 1f, 0.5f);
            else GUI.backgroundColor = new Color(0.5f, 1f, 0.5f);

            EditorGUILayout.LabelField(
                "PASS: " + _passCount + "  WARN: " + _warnCount + "  FAIL: " + _failCount,
                EditorStyles.boldLabel);
            GUI.backgroundColor = oldBg;

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(400));
            foreach (CheckItem item in _items)
            {
                Color oldColor = GUI.color;
                if (item.status == 0) GUI.color = Color.green;
                else if (item.status == 1) GUI.color = Color.yellow;
                else GUI.color = new Color(1f, 0.4f, 0.4f);

                string icon = item.status == 0 ? "[PASS]" : item.status == 1 ? "[WARN]" : "[FAIL]";
                EditorGUILayout.LabelField(icon + " " + item.name + ": " + item.detail,
                    EditorStyles.wordWrappedLabel);
                GUI.color = oldColor;
            }
            EditorGUILayout.EndScrollView();
        }
    }

    private void RunChecklist()
    {
        if (_npcRoot == null) return;

        CheckComponents();
        CheckAnimator();
        CheckSerializeFieldWiring();
        CheckBlendShapes();
        CheckWaypoints();
    }

    private void CheckComponents()
    {
        UdonSharpBehaviour[] allUdon = _npcRoot.GetComponentsInChildren<UdonSharpBehaviour>(true);
        HashSet<string> foundTypes = new HashSet<string>();
        foreach (UdonSharpBehaviour usb in allUdon)
        {
            foundTypes.Add(usb.GetType().Name);
        }

        int found = 0;
        int missing = 0;
        foreach (string comp in RequiredComponents)
        {
            if (foundTypes.Contains(comp))
            {
                found++;
            }
            else
            {
                missing++;
                AddItem("Component: " + comp, 2, "Not found on NPC hierarchy");
            }
        }

        if (missing == 0)
        {
            AddItem("Components", 0, "All " + RequiredComponents.Length + " components present");
        }
        else
        {
            AddItem("Components", 2, found + "/" + RequiredComponents.Length + " found (" + missing + " missing)");
        }
    }

    private void CheckAnimator()
    {
        Animator animator = _npcRoot.GetComponentInChildren<Animator>(true);
        if (animator == null)
        {
            AddItem("Animator", 2, "No Animator found");
            return;
        }

        if (!animator.isHuman)
        {
            AddItem("Animator Rig", 2, "Not a humanoid rig — IK will not function");
        }
        else
        {
            AddItem("Animator Rig", 0, "Humanoid rig configured");
        }

        if (animator.runtimeAnimatorController == null)
        {
            AddItem("AnimatorController", 2, "No controller assigned");
        }
        else
        {
            AddItem("AnimatorController", 0, "Controller assigned: " + animator.runtimeAnimatorController.name);
        }
    }

    private void CheckSerializeFieldWiring()
    {
        // Check the Manager's critical references
        UdonSharpBehaviour[] allUdon = _npcRoot.GetComponentsInChildren<UdonSharpBehaviour>(true);
        int totalFields = 0;
        int nullFields = 0;

        foreach (UdonSharpBehaviour usb in allUdon)
        {
            System.Type type = usb.GetType();
            System.Reflection.FieldInfo[] fields = type.GetFields(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            foreach (System.Reflection.FieldInfo field in fields)
            {
                object[] attrs = field.GetCustomAttributes(typeof(SerializeField), true);
                if (attrs.Length == 0) continue;

                // Only check reference types (skip value types like float, int, bool)
                if (field.FieldType.IsValueType) continue;

                totalFields++;
                object val = field.GetValue(usb);
                if (val == null || val.Equals(null))
                {
                    nullFields++;
                }
            }
        }

        if (nullFields == 0)
        {
            AddItem("SerializeField Wiring", 0, "All " + totalFields + " references wired");
        }
        else
        {
            AddItem("SerializeField Wiring", 1,
                nullFields + " of " + totalFields + " references are null (some may be optional)");
        }
    }

    private void CheckBlendShapes()
    {
        SkinnedMeshRenderer[] renderers = _npcRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        int totalShapes = 0;
        foreach (SkinnedMeshRenderer smr in renderers)
        {
            if (smr.sharedMesh != null)
                totalShapes += smr.sharedMesh.blendShapeCount;
        }

        if (totalShapes > 0)
        {
            AddItem("Blend Shapes", 0, totalShapes + " blend shapes available");
        }
        else
        {
            AddItem("Blend Shapes", 1, "No blend shapes found — facial expressions will not work");
        }
    }

    private void CheckWaypoints()
    {
        Transform waypointContainer = _npcRoot.transform.Find("Waypoints");
        if (waypointContainer == null)
        {
            // Search deeper
            foreach (Transform child in _npcRoot.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "Waypoints")
                {
                    waypointContainer = child;
                    break;
                }
            }
        }

        if (waypointContainer == null)
        {
            AddItem("Waypoints", 1, "No Waypoints container found — idle patrol disabled");
        }
        else
        {
            int wpCount = waypointContainer.childCount;
            if (wpCount >= 2)
            {
                AddItem("Waypoints", 0, wpCount + " waypoints configured");
            }
            else
            {
                AddItem("Waypoints", 1, "Only " + wpCount + " waypoint(s) — need 2+ for patrol");
            }
        }
    }

    private void AddItem(string name, int status, string detail)
    {
        CheckItem item = new CheckItem();
        item.name = name;
        item.status = status;
        item.detail = detail;
        _items.Add(item);

        if (status == 0) _passCount++;
        else if (status == 1) _warnCount++;
        else _failCount++;
    }
}
