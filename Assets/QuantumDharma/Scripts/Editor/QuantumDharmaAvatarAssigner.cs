using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Scans an NPC hierarchy for SkinnedMeshRenderer components and attempts to
/// auto-assign avatar model references and blend shape mappings for
/// FacialExpressionController.
///
/// Searches for common blend shape naming patterns (VRM, VRChat, generic)
/// and reports confidence for each mapping.
///
/// Menu: Quantum Dharma > Auto-Assign Avatar Model
/// </summary>
public class QuantumDharmaAvatarAssigner : EditorWindow
{
    private GameObject _npcRoot;
    private Vector2 _scrollPos;
    private readonly List<string> _log = new List<string>();
    private readonly List<BlendShapeMapping> _mappings = new List<BlendShapeMapping>();

    // Common blend shape patterns per emotion
    private static readonly string[][] SmilePatterns = new string[][]
    {
        new string[] { "Fcl_MTH_Smile", "vrc.v_aa", "mouth_smile", "Smile", "Joy", "Happy" },
        new string[] { "Fcl_BRW_Joy", "brow_joy", "BrowUp", "BrowsUp" },
    };

    private static readonly string[][] SadPatterns = new string[][]
    {
        new string[] { "Fcl_MTH_Sad", "mouth_sad", "Sad", "Sorrow", "Frown" },
        new string[] { "Fcl_BRW_Sorrow", "brow_sad", "BrowDown", "BrowsSad" },
    };

    private static readonly string[][] AngryPatterns = new string[][]
    {
        new string[] { "Fcl_MTH_Angry", "mouth_angry", "Angry" },
        new string[] { "Fcl_BRW_Angry", "brow_angry", "BrowAngry" },
    };

    private static readonly string[][] SurprisedPatterns = new string[][]
    {
        new string[] { "Fcl_MTH_Surprised", "mouth_oh", "Surprised", "Oh" },
        new string[] { "Fcl_BRW_Surprised", "brow_up", "BrowSurprise" },
    };

    private static readonly string[] BlinkPatterns = new string[]
    {
        "Fcl_EYE_Close", "vrc.blink", "Blink", "EyesClosed", "eye_close", "Fcl_ALL_Close"
    };

    private static readonly string[] MouthOpenPatterns = new string[]
    {
        "Fcl_MTH_Open", "vrc.v_aa", "MouthOpen", "mouth_open", "A", "Fcl_MTH_A"
    };

    private struct BlendShapeMapping
    {
        public string emotionName;
        public string blendShapeName;
        public int blendShapeIndex;
        public float confidence;
    }

    [MenuItem("Quantum Dharma/Auto-Assign Avatar Model")]
    private static void ShowWindow()
    {
        GetWindow<QuantumDharmaAvatarAssigner>("Avatar Assigner");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Avatar Model Auto-Assigner", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Select an NPC root object. This tool will scan for SkinnedMeshRenderer "
            + "components and attempt to auto-map blend shapes for facial expressions.",
            MessageType.Info);
        EditorGUILayout.Space();

        _npcRoot = (GameObject)EditorGUILayout.ObjectField("NPC Root", _npcRoot, typeof(GameObject), true);

        EditorGUILayout.Space();

        EditorGUI.BeginDisabledGroup(_npcRoot == null);
        if (GUILayout.Button("Scan & Map Blend Shapes", GUILayout.Height(25)))
        {
            _log.Clear();
            _mappings.Clear();
            ScanAndMap();
        }
        EditorGUI.EndDisabledGroup();

        // Results
        if (_mappings.Count > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Detected Mappings:", EditorStyles.boldLabel);
            foreach (BlendShapeMapping m in _mappings)
            {
                string confStr = m.confidence >= 0.8f ? "HIGH"
                    : m.confidence >= 0.5f ? "MEDIUM" : "LOW";
                Color oldColor = GUI.color;
                GUI.color = m.confidence >= 0.8f ? Color.green
                    : m.confidence >= 0.5f ? Color.yellow : Color.red;
                EditorGUILayout.LabelField(
                    m.emotionName + " -> " + m.blendShapeName
                    + " (idx " + m.blendShapeIndex + ", " + confStr + ")");
                GUI.color = oldColor;
            }
        }

        // Log
        if (_log.Count > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Log:", EditorStyles.boldLabel);
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(200));
            foreach (string line in _log)
            {
                EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndScrollView();
        }
    }

    private void ScanAndMap()
    {
        if (_npcRoot == null) return;

        SkinnedMeshRenderer[] renderers = _npcRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Log("Found " + renderers.Length + " SkinnedMeshRenderer(s)");

        SkinnedMeshRenderer bestFaceMesh = null;
        int bestBlendShapeCount = 0;

        foreach (SkinnedMeshRenderer smr in renderers)
        {
            if (smr.sharedMesh == null) continue;
            int count = smr.sharedMesh.blendShapeCount;
            Log("  " + smr.gameObject.name + ": " + count + " blend shapes");

            if (count > bestBlendShapeCount)
            {
                bestBlendShapeCount = count;
                bestFaceMesh = smr;
            }
        }

        if (bestFaceMesh == null)
        {
            Log("ERROR: No SkinnedMeshRenderer with blend shapes found");
            return;
        }

        Log("Selected face mesh: " + bestFaceMesh.gameObject.name + " (" + bestBlendShapeCount + " shapes)");
        Log("");

        // Collect blend shape names
        Mesh mesh = bestFaceMesh.sharedMesh;
        string[] shapeNames = new string[mesh.blendShapeCount];
        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
            shapeNames[i] = mesh.GetBlendShapeName(i);
        }

        // Map emotions
        TryMapEmotion("Smile", SmilePatterns[0], shapeNames);
        TryMapEmotion("SmileBrow", SmilePatterns[1], shapeNames);
        TryMapEmotion("Sad", SadPatterns[0], shapeNames);
        TryMapEmotion("SadBrow", SadPatterns[1], shapeNames);
        TryMapEmotion("Angry", AngryPatterns[0], shapeNames);
        TryMapEmotion("AngryBrow", AngryPatterns[1], shapeNames);
        TryMapEmotion("Surprised", SurprisedPatterns[0], shapeNames);
        TryMapEmotion("SurprisedBrow", SurprisedPatterns[1], shapeNames);
        TryMapEmotion("Blink", BlinkPatterns, shapeNames);
        TryMapEmotion("MouthOpen", MouthOpenPatterns, shapeNames);

        Log("");
        Log("Mapping complete. " + _mappings.Count + " blend shapes found.");
        if (_mappings.Count < 5)
        {
            Log("WARNING: Low mapping count. Manual assignment may be needed.");
        }
    }

    private void TryMapEmotion(string emotionName, string[] patterns, string[] shapeNames)
    {
        for (int p = 0; p < patterns.Length; p++)
        {
            string pattern = patterns[p].ToLowerInvariant();
            for (int i = 0; i < shapeNames.Length; i++)
            {
                if (shapeNames[i].ToLowerInvariant().Contains(pattern))
                {
                    float confidence = p == 0 ? 0.9f : 0.5f + (0.4f / (p + 1));
                    BlendShapeMapping m = new BlendShapeMapping();
                    m.emotionName = emotionName;
                    m.blendShapeName = shapeNames[i];
                    m.blendShapeIndex = i;
                    m.confidence = confidence;
                    _mappings.Add(m);
                    Log("  " + emotionName + " -> " + shapeNames[i] + " (confidence: " + confidence.ToString("F2") + ")");
                    return;
                }
            }
        }
        Log("  " + emotionName + " -> NOT FOUND");
    }

    private void Log(string msg)
    {
        _log.Add(msg);
    }
}
