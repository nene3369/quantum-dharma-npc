using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UdonSharp;

/// <summary>
/// Auto-wires all SerializeField component references on a Quantum Dharma NPC hierarchy.
/// Menu: Quantum Dharma > Auto-Wire NPC
/// Finds the QuantumDharmaManager on the selected GameObject (or its parent),
/// discovers all UdonSharpBehaviour components, and resolves cross-references
/// using GetComponent/GetComponentInChildren.
/// </summary>
public class QuantumDharmaAutoWirer : EditorWindow
{
    private GameObject _npcRoot;
    private bool _overwriteExisting;
    private Vector2 _scrollPos;
    private readonly List<string> _log = new List<string>();
    private int _wiredCount;
    private int _skippedCount;
    private int _failedCount;

    [MenuItem("Quantum Dharma/Auto-Wire NPC")]
    private static void ShowWindow()
    {
        var window = GetWindow<QuantumDharmaAutoWirer>("QD Auto-Wirer");
        window.minSize = new Vector2(400, 300);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Quantum Dharma NPC Auto-Wirer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Automatically wires all SerializeField component references " +
            "on the NPC hierarchy. Select the root NPC GameObject.",
            MessageType.Info);

        EditorGUILayout.Space(4);

        _npcRoot = (GameObject)EditorGUILayout.ObjectField(
            "NPC Root", _npcRoot, typeof(GameObject), true);

        _overwriteExisting = EditorGUILayout.Toggle(
            "Overwrite Existing Refs", _overwriteExisting);

        EditorGUILayout.Space(4);

        using (new EditorGUI.DisabledGroupScope(_npcRoot == null))
        {
            if (GUILayout.Button("Auto-Wire All References", GUILayout.Height(30)))
            {
                RunAutoWire();
            }

            EditorGUILayout.Space(4);

            if (GUILayout.Button("Auto-Map BlendShapes", GUILayout.Height(24)))
            {
                _log.Clear();
                _wiredCount = 0;
                _log.Add("=== BlendShape Auto-Mapping ===");
                _wiredCount = AutoMapBlendShapes(_npcRoot, _log);
                _log.Add("[DONE] " + _wiredCount + " blend shapes mapped");
            }
        }

        EditorGUILayout.Space(4);
        if (GUILayout.Button("Wire Peer NPCs (Scene-wide)", GUILayout.Height(24)))
        {
            _log.Clear();
            _wiredCount = 0;
            _log.Add("=== Multi-NPC Peer Wiring ===");
            _wiredCount = WirePeerNPCs(_log);
            _log.Add("[DONE] " + _wiredCount + " peer references wired");
        }

        if (_log.Count > 0)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(
                "Result: " + _wiredCount + " wired, " +
                _skippedCount + " skipped, " + _failedCount + " unresolved",
                EditorStyles.boldLabel);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            foreach (string entry in _log)
            {
                EditorGUILayout.LabelField(entry, EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
        }
    }

    private void RunAutoWire()
    {
        _log.Clear();
        _wiredCount = 0;
        _skippedCount = 0;
        _failedCount = 0;

        if (_npcRoot == null)
        {
            _log.Add("[ERROR] No NPC root selected.");
            return;
        }

        // Build a map of Type -> Component for all UdonSharpBehaviours in hierarchy
        UdonSharpBehaviour[] allBehaviours =
            _npcRoot.GetComponentsInChildren<UdonSharpBehaviour>(true);

        // Type -> list of components (usually 1, but peers can have multiples)
        Dictionary<Type, List<Component>> typeMap = new Dictionary<Type, List<Component>>();
        foreach (UdonSharpBehaviour usb in allBehaviours)
        {
            Type t = usb.GetType();
            if (!typeMap.ContainsKey(t))
            {
                typeMap[t] = new List<Component>();
            }
            typeMap[t].Add(usb);
        }

        // Also collect standard Unity components on the root
        CollectUnityComponents(typeMap);

        _log.Add("[INFO] Found " + allBehaviours.Length + " UdonSharpBehaviours");
        _log.Add("[INFO] Found " + typeMap.Count + " unique component types");
        _log.Add("---");

        int totalFields = 0;

        // For each behaviour, iterate its SerializeField fields
        foreach (UdonSharpBehaviour usb in allBehaviours)
        {
            Type compType = usb.GetType();
            SerializedObject so = new SerializedObject(usb);

            FieldInfo[] fields = compType.GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            foreach (FieldInfo field in fields)
            {
                // Only process [SerializeField] fields
                if (field.GetCustomAttribute<SerializeField>() == null &&
                    !field.IsPublic)
                {
                    continue;
                }

                Type fieldType = field.FieldType;

                // Skip non-component fields (primitives, strings, arrays of primitives, etc.)
                if (!typeof(Component).IsAssignableFrom(fieldType))
                {
                    continue;
                }

                // Skip fields that are peer/external references
                if (IsPeerField(compType.Name, field.Name))
                {
                    _log.Add("[SKIP] " + compType.Name + "." + field.Name +
                             " (external/peer reference)");
                    _skippedCount++;
                    continue;
                }

                totalFields++;

                SerializedProperty prop = so.FindProperty(field.Name);
                if (prop == null)
                {
                    _log.Add("[WARN] " + compType.Name + "." + field.Name +
                             " - SerializedProperty not found");
                    _failedCount++;
                    continue;
                }

                // Check if already assigned
                if (!_overwriteExisting && prop.objectReferenceValue != null)
                {
                    _skippedCount++;
                    continue;
                }

                // Try to resolve from type map
                Component resolved = ResolveComponent(fieldType, typeMap);

                if (resolved != null)
                {
                    prop.objectReferenceValue = resolved;
                    so.ApplyModifiedProperties();
                    _log.Add("[OK] " + compType.Name + "." + field.Name +
                             " -> " + resolved.GetType().Name);
                    _wiredCount++;
                }
                else
                {
                    _log.Add("[MISS] " + compType.Name + "." + field.Name +
                             " (" + fieldType.Name + ") - no component found");
                    _failedCount++;
                }
            }
        }

        _log.Add("---");
        _log.Add("[DONE] Processed " + totalFields + " component fields");

        if (_wiredCount > 0)
        {
            EditorUtility.SetDirty(_npcRoot);
        }
    }

    private void CollectUnityComponents(Dictionary<Type, List<Component>> typeMap)
    {
        CollectType<Animator>(typeMap);
        CollectType<AudioSource>(typeMap);
        CollectType<SkinnedMeshRenderer>(typeMap);
        CollectType<LineRenderer>(typeMap);
        CollectType<ParticleSystem>(typeMap);
        CollectType<Light>(typeMap);
    }

    private void CollectType<T>(Dictionary<Type, List<Component>> typeMap) where T : Component
    {
        T[] found = _npcRoot.GetComponentsInChildren<T>(true);
        if (found.Length > 0)
        {
            Type t = typeof(T);
            if (!typeMap.ContainsKey(t))
            {
                typeMap[t] = new List<Component>();
            }
            foreach (T c in found)
            {
                typeMap[t].Add(c);
            }
        }
    }

    private Component ResolveComponent(Type fieldType,
        Dictionary<Type, List<Component>> typeMap)
    {
        if (typeMap.ContainsKey(fieldType))
        {
            List<Component> candidates = typeMap[fieldType];
            if (candidates.Count >= 1)
            {
                return candidates[0];
            }
        }

        // Fallback: check if any registered type is assignable to the field type
        foreach (KeyValuePair<Type, List<Component>> kvp in typeMap)
        {
            if (fieldType.IsAssignableFrom(kvp.Key) && kvp.Value.Count > 0)
            {
                return kvp.Value[0];
            }
        }

        return null;
    }

    /// <summary>
    /// Fields that reference external/peer objects (other NPCs, etc.)
    /// and should NOT be auto-wired from the same hierarchy.
    /// </summary>
    private bool IsPeerField(string className, string fieldName)
    {
        // CollectiveMemory peer session memories
        if (className == "CollectiveMemory" &&
            (fieldName == "_peerMemory0" || fieldName == "_peerMemory1" ||
             fieldName == "_peerMemory2" || fieldName == "_peerMemory3"))
        {
            return true;
        }

        // MultiNPCRelay peer relays
        if (className == "MultiNPCRelay" &&
            (fieldName == "_peer0" || fieldName == "_peer1" ||
             fieldName == "_peer2" || fieldName == "_peer3"))
        {
            return true;
        }

        // PersonalityInstaller avatar models (user must set manually)
        if (className == "PersonalityInstaller" && fieldName == "_avatarModels")
        {
            return true;
        }

        // IdleWaypoints waypoints (user must place in scene)
        if (className == "IdleWaypoints" && fieldName == "_waypoints")
        {
            return true;
        }

        // TrustVisualizer renderers (user must assign mesh renderers)
        if (className == "TrustVisualizer" && fieldName == "_renderers")
        {
            return true;
        }

        return false;
    }

    // ══════════════════════════════════════════════════════════════
    // BlendShape Auto-Mapping
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Scans SkinnedMeshRenderer blend shapes for common naming patterns
    /// (VRM, VRChat, generic) and auto-assigns indices to
    /// FacialExpressionController fields.
    /// </summary>
    public static int AutoMapBlendShapes(GameObject npcRoot, List<string> log)
    {
        SkinnedMeshRenderer smr =
            npcRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr == null || smr.sharedMesh == null)
        {
            if (log != null)
                log.Add("[SKIP] No SkinnedMeshRenderer with mesh found");
            return 0;
        }

        Mesh mesh = smr.sharedMesh;
        int count = mesh.blendShapeCount;
        if (count == 0)
        {
            if (log != null)
                log.Add("[SKIP] Mesh has no blend shapes");
            return 0;
        }

        // Build lowercase name -> index map
        Dictionary<string, int> nameToIndex = new Dictionary<string, int>();
        for (int i = 0; i < count; i++)
        {
            string bsName = mesh.GetBlendShapeName(i).ToLowerInvariant();
            if (!nameToIndex.ContainsKey(bsName))
            {
                nameToIndex[bsName] = i;
            }
        }

        // Pattern map: field name -> search patterns (checked in priority order)
        string[][] fieldPatterns = new string[][]
        {
            // _blendJoy
            new string[] { "_blendJoy",
                "joy", "happy", "smile", "fcl_all_joy", "fcl_mth_joy",
                "blendshape.joy", "blendshape.happy" },
            // _blendSorrow
            new string[] { "_blendSorrow",
                "sorrow", "sad", "sadness", "fcl_all_sorrow",
                "blendshape.sorrow", "blendshape.sad" },
            // _blendAngry
            new string[] { "_blendAngry",
                "angry", "anger", "fcl_all_angry",
                "blendshape.angry", "blendshape.anger" },
            // _blendSurprise
            new string[] { "_blendSurprise",
                "surprise", "surprised", "fun", "fcl_all_surprised",
                "fcl_all_fun", "blendshape.surprise", "blendshape.fun" },
            // _blendFear
            new string[] { "_blendFear",
                "fear", "afraid", "scared", "fcl_all_fear",
                "blendshape.fear" },
            // _blendMouthOpen
            new string[] { "_blendMouthOpen",
                "vrc.v_aa", "a", "aa", "mouth_open", "mouthopen",
                "fcl_mth_a", "mth_a", "blendshape.a",
                "blendshape1.vrc.v_aa" },
            // _blendMouthOh
            new string[] { "_blendMouthOh",
                "vrc.v_oh", "o", "oh", "mouth_o", "mouthoh",
                "fcl_mth_o", "mth_o", "blendshape.o",
                "blendshape1.vrc.v_oh" }
        };

        // Find FacialExpressionController
        UdonSharpBehaviour[] behaviours =
            npcRoot.GetComponentsInChildren<UdonSharpBehaviour>(true);
        UdonSharpBehaviour fec = null;
        foreach (UdonSharpBehaviour usb in behaviours)
        {
            if (usb.GetType().Name == "FacialExpressionController")
            {
                fec = usb;
                break;
            }
        }

        if (fec == null)
        {
            if (log != null)
                log.Add("[SKIP] FacialExpressionController not found");
            return 0;
        }

        SerializedObject so = new SerializedObject(fec);
        int mapped = 0;

        foreach (string[] pattern in fieldPatterns)
        {
            string fieldName = pattern[0];
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null) continue;

            // Skip if already set to a valid index
            if (prop.intValue >= 0 && prop.intValue < count)
            {
                if (log != null)
                    log.Add("[KEEP] " + fieldName + " = " + prop.intValue +
                            " (" + mesh.GetBlendShapeName(prop.intValue) + ")");
                continue;
            }

            // Search patterns
            int foundIndex = -1;
            string foundName = "";
            for (int p = 1; p < pattern.Length; p++)
            {
                string search = pattern[p].ToLowerInvariant();

                // Exact match first
                if (nameToIndex.ContainsKey(search))
                {
                    foundIndex = nameToIndex[search];
                    foundName = search;
                    break;
                }

                // Substring / contains match
                foreach (KeyValuePair<string, int> kvp in nameToIndex)
                {
                    if (kvp.Key.Contains(search))
                    {
                        foundIndex = kvp.Value;
                        foundName = kvp.Key;
                        break;
                    }
                }
                if (foundIndex >= 0) break;
            }

            if (foundIndex >= 0)
            {
                prop.intValue = foundIndex;
                so.ApplyModifiedProperties();
                mapped++;
                if (log != null)
                    log.Add("[MAP] " + fieldName + " = " + foundIndex +
                            " (" + foundName + ")");
            }
            else
            {
                if (log != null)
                    log.Add("[MISS] " + fieldName + " - no matching blend shape");
            }
        }

        // Also wire _faceMesh reference if not set
        SerializedProperty faceMeshProp = so.FindProperty("_faceMesh");
        if (faceMeshProp != null && faceMeshProp.objectReferenceValue == null)
        {
            faceMeshProp.objectReferenceValue = smr;
            so.ApplyModifiedProperties();
            mapped++;
            if (log != null)
                log.Add("[MAP] _faceMesh -> " + smr.name);
        }

        return mapped;
    }

    // ══════════════════════════════════════════════════════════════
    // Multi-NPC Peer Wiring
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Finds all Quantum Dharma NPCs in the scene and auto-wires
    /// CollectiveMemory._peerMemory and MultiNPCRelay._peer references.
    /// </summary>
    public static int WirePeerNPCs(List<string> log)
    {
        // Find all root NPCs by looking for QuantumDharmaManager
        UdonSharpBehaviour[] allBehaviours =
            UnityEngine.Object.FindObjectsOfType<UdonSharpBehaviour>();

        List<GameObject> npcRoots = new List<GameObject>();
        foreach (UdonSharpBehaviour usb in allBehaviours)
        {
            if (usb.GetType().Name == "QuantumDharmaManager")
            {
                npcRoots.Add(usb.gameObject);
            }
        }

        if (npcRoots.Count < 2)
        {
            if (log != null)
                log.Add("[INFO] Found " + npcRoots.Count +
                         " NPC(s) in scene. Need 2+ for peer wiring.");
            return 0;
        }

        if (log != null)
            log.Add("[INFO] Found " + npcRoots.Count + " NPCs in scene");

        int wired = 0;

        // For each NPC, wire peers
        for (int i = 0; i < npcRoots.Count; i++)
        {
            GameObject npc = npcRoots[i];
            UdonSharpBehaviour[] components =
                npc.GetComponentsInChildren<UdonSharpBehaviour>(true);

            // Build peer list (all other NPCs, up to 4)
            List<GameObject> peers = new List<GameObject>();
            for (int j = 0; j < npcRoots.Count && peers.Count < 4; j++)
            {
                if (j != i) peers.Add(npcRoots[j]);
            }

            foreach (UdonSharpBehaviour usb in components)
            {
                string typeName = usb.GetType().Name;

                if (typeName == "CollectiveMemory")
                {
                    wired += WirePeerSlots(usb, peers,
                        "SessionMemory", "_localMemory",
                        new string[] { "_peerMemory0", "_peerMemory1",
                                       "_peerMemory2", "_peerMemory3" },
                        npc.name, log);
                }
                else if (typeName == "MultiNPCRelay")
                {
                    wired += WirePeerSlots(usb, peers,
                        "MultiNPCRelay", null,
                        new string[] { "_peer0", "_peer1",
                                       "_peer2", "_peer3" },
                        npc.name, log);
                }
            }
        }

        return wired;
    }

    private static int WirePeerSlots(UdonSharpBehaviour usb,
        List<GameObject> peers, string peerTypeName, string skipFieldName,
        string[] slotFields, string npcName, List<string> log)
    {
        SerializedObject so = new SerializedObject(usb);
        int wired = 0;

        for (int p = 0; p < slotFields.Length; p++)
        {
            SerializedProperty prop = so.FindProperty(slotFields[p]);
            if (prop == null) continue;

            // Skip already assigned
            if (prop.objectReferenceValue != null) continue;

            if (p >= peers.Count) break;

            // Find the target component on the peer NPC
            UdonSharpBehaviour[] peerComponents =
                peers[p].GetComponentsInChildren<UdonSharpBehaviour>(true);

            foreach (UdonSharpBehaviour peerComp in peerComponents)
            {
                if (peerComp.GetType().Name == peerTypeName)
                {
                    prop.objectReferenceValue = peerComp;
                    so.ApplyModifiedProperties();
                    wired++;
                    if (log != null)
                        log.Add("[PEER] " + npcName + "." +
                                usb.GetType().Name + "." + slotFields[p] +
                                " -> " + peers[p].name);
                    break;
                }
            }
        }

        return wired;
    }
}
