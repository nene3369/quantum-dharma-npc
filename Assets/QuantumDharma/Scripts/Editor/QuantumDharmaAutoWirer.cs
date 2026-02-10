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
}
