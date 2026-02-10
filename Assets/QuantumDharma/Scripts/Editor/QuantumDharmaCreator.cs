using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UdonSharp;

/// <summary>
/// One-click NPC factory: creates a full Quantum Dharma NPC hierarchy
/// with all 49 components, required Unity components, touch zones,
/// waypoints, UI canvas, and auto-wires everything.
/// Menu: Quantum Dharma > Create Full NPC
/// </summary>
public class QuantumDharmaCreator : EditorWindow
{
    private string _npcName = "QuantumDharmaNPC";
    private int _waypointCount = 4;
    private bool _createDebugUI = true;
    private bool _autoWire = true;
    private Vector2 _scrollPos;
    private readonly List<string> _log = new List<string>();

    // All 49 UdonSharpBehaviour class names, ordered by dependency
    // (components referenced by many others are added first)
    private static readonly string[] ComponentNames = new string[]
    {
        // ── Core (29) ──
        "QuantumDharmaManager",
        "FreeEnergyCalculator",
        "BeliefState",
        "QuantumDharmaNPC",
        "SessionMemory",
        "SpeechOrchestrator",
        "DreamState",
        "DreamNarrative",
        "AdaptivePersonality",
        "CuriosityDrive",
        "ContextualUtterance",
        "GroupDynamics",
        "EmotionalContagion",
        "AttentionSystem",
        "HabitFormation",
        "MultiNPCRelay",
        "SharedRitual",
        "CollectiveMemory",
        "GiftEconomy",
        "NormFormation",
        "OralHistory",
        "NameGiving",
        "Mythology",
        "CompanionMemory",
        "FarewellBehavior",
        "PersonalityPreset",
        "PersonalityInstaller",
        "AutonomousGoals",
        "EnvironmentAwareness",
        "ImitationLearning",
        // ── Perception (7) ──
        "PlayerSensor",
        "MarkovBlanket",
        "HandProximityDetector",
        "PostureDetector",
        "TouchSensor",
        "GiftReceiver",
        "VoiceDetector",
        // ── Action (9) ──
        "NPCMotor",
        "LookAtController",
        "EmotionAnimator",
        "MirrorBehavior",
        "GestureController",
        "ProximityAudio",
        "IdleWaypoints",
        "FacialExpressionController",
        "UpperBodyIK",
        // ── UI (3) ──
        "DebugOverlay",
        "FreeEnergyVisualizer",
        "TrustVisualizer"
    };

    [MenuItem("Quantum Dharma/Create Full NPC")]
    private static void ShowWindow()
    {
        var window = GetWindow<QuantumDharmaCreator>("QD NPC Creator");
        window.minSize = new Vector2(450, 500);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Quantum Dharma NPC Creator", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Creates a complete NPC hierarchy with all 49 components, " +
            "Unity components, touch zone, waypoints, and UI.\n" +
            "After creation, auto-wires all 165+ SerializeField references.",
            MessageType.Info);

        EditorGUILayout.Space(4);
        _npcName = EditorGUILayout.TextField("NPC Name", _npcName);
        _waypointCount = EditorGUILayout.IntSlider("Waypoints", _waypointCount, 2, 8);
        _createDebugUI = EditorGUILayout.Toggle("Create Debug UI", _createDebugUI);
        _autoWire = EditorGUILayout.Toggle("Auto-Wire After Creation", _autoWire);

        EditorGUILayout.Space(8);

        if (GUILayout.Button("Create Full NPC", GUILayout.Height(35)))
        {
            CreateNPC();
        }

        if (_log.Count > 0)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Creation Log", EditorStyles.boldLabel);
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            foreach (string entry in _log)
            {
                EditorGUILayout.LabelField(entry, EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
        }
    }

    private void CreateNPC()
    {
        _log.Clear();
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();

        // ── 1. Build type lookup from loaded assemblies ──
        Dictionary<string, Type> typeMap = BuildTypeMap();

        // ── 2. Create root GameObject ──
        GameObject root = new GameObject(_npcName);
        Undo.RegisterCreatedObjectUndo(root, "Create Quantum Dharma NPC");
        _log.Add("[ROOT] " + _npcName);

        // ── 3. Add required Unity components ──
        Animator animator = root.AddComponent<Animator>();
        animator.applyRootMotion = false;
        _log.Add("[UNITY] Animator");

        AudioSource audioSource = root.AddComponent<AudioSource>();
        audioSource.spatialBlend = 1f;       // 3D
        audioSource.maxDistance = 15f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.playOnAwake = false;
        _log.Add("[UNITY] AudioSource (3D spatial)");

        // ── 4. Add all 49 UdonSharpBehaviour components ──
        int addedCount = 0;
        int failedCount = 0;

        foreach (string className in ComponentNames)
        {
            if (!typeMap.ContainsKey(className))
            {
                _log.Add("[FAIL] " + className + " - type not found in assemblies");
                failedCount++;
                continue;
            }

            Type compType = typeMap[className];

            // Check if already exists (shouldn't, but safety)
            if (root.GetComponent(compType) != null)
            {
                _log.Add("[SKIP] " + className + " - already exists");
                continue;
            }

            root.AddComponent(compType);
            addedCount++;
        }

        _log.Add("[INFO] Added " + addedCount + "/" + ComponentNames.Length +
                 " components" + (failedCount > 0 ? " (" + failedCount + " failed)" : ""));

        // ── 5. Create child objects ──

        // Waypoints
        GameObject waypointsParent = CreateChild(root, "Waypoints");
        float radius = 5f;
        for (int i = 0; i < _waypointCount; i++)
        {
            GameObject wp = CreateChild(waypointsParent, "Waypoint_" + i);
            float angle = (2f * Mathf.PI * i) / _waypointCount;
            wp.transform.localPosition = new Vector3(
                Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }
        _log.Add("[CHILD] Waypoints (" + _waypointCount + " points, radius " + radius + "m)");

        // Wire waypoints to IdleWaypoints via SerializedObject
        WireWaypoints(root, waypointsParent);

        // MarkovBlanket gizmo sphere (visual only in editor)
        GameObject blanketVis = CreateChild(root, "MarkovBlanketVisual");
        blanketVis.transform.localPosition = Vector3.zero;
        _log.Add("[CHILD] MarkovBlanketVisual");

        // Particle system children
        GameObject emotionParticles = CreateChild(root, "EmotionParticles");
        emotionParticles.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule epMain =
            emotionParticles.GetComponent<ParticleSystem>().main;
        epMain.playOnAwake = false;
        epMain.maxParticles = 20;
        _log.Add("[CHILD] EmotionParticles (ParticleSystem)");

        GameObject dreamParticles = CreateChild(root, "DreamParticles");
        dreamParticles.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule dpMain =
            dreamParticles.GetComponent<ParticleSystem>().main;
        dpMain.playOnAwake = false;
        dpMain.maxParticles = 10;
        _log.Add("[CHILD] DreamParticles (ParticleSystem)");

        GameObject giftParticles = CreateChild(root, "GiftBurstParticles");
        giftParticles.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule gpMain =
            giftParticles.GetComponent<ParticleSystem>().main;
        gpMain.playOnAwake = false;
        gpMain.maxParticles = 30;
        gpMain.startLifetime = 1.5f;
        _log.Add("[CHILD] GiftBurstParticles (ParticleSystem)");

        // FreeEnergyVisualizer ring
        GameObject feRing = CreateChild(root, "FreeEnergyRing");
        LineRenderer lr = feRing.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.widthMultiplier = 0.02f;
        _log.Add("[CHILD] FreeEnergyRing (LineRenderer)");

        // Debug UI
        if (_createDebugUI)
        {
            CreateDebugUI(root);
        }

        // ── 6. Wire particle systems and LineRenderer ──
        WireParticlesAndVisuals(root, emotionParticles, dreamParticles,
            giftParticles, feRing);

        // ── 7. Auto-wire all component cross-references ──
        if (_autoWire)
        {
            _log.Add("---");
            _log.Add("[AUTO-WIRE] Running auto-wirer...");
            int wired = RunAutoWire(root);
            _log.Add("[AUTO-WIRE] " + wired + " references connected");
        }

        // ── 8. Select the created NPC ──
        Selection.activeGameObject = root;
        Undo.CollapseUndoOperations(undoGroup);
        Undo.SetCurrentGroupName("Create Quantum Dharma NPC");

        _log.Add("===");
        _log.Add("[DONE] NPC created. Next steps:");
        _log.Add("  1. Assign avatar model as child of " + _npcName);
        _log.Add("  2. Run: Quantum Dharma > Create Animator Controller");
        _log.Add("  3. Assign Animator Controller to Animator component");
        _log.Add("  4. Set blend shape indices on FacialExpressionController");
        _log.Add("  5. Run: Quantum Dharma > Validate NPC Setup");

        Debug.Log("[QuantumDharma] NPC '" + _npcName + "' created with " +
                  addedCount + " components");
    }

    private Dictionary<string, Type> BuildTypeMap()
    {
        Dictionary<string, Type> map = new Dictionary<string, Type>();
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (Assembly asm in assemblies)
        {
            // Skip system / Unity internal assemblies for performance
            string asmName = asm.GetName().Name;
            if (asmName.StartsWith("System") || asmName.StartsWith("Unity") ||
                asmName.StartsWith("mscorlib") || asmName.StartsWith("Mono"))
            {
                continue;
            }

            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch
            {
                continue;
            }

            foreach (Type t in types)
            {
                if (typeof(UdonSharpBehaviour).IsAssignableFrom(t) && !t.IsAbstract)
                {
                    if (!map.ContainsKey(t.Name))
                    {
                        map[t.Name] = t;
                    }
                }
            }
        }

        _log.Add("[INFO] Found " + map.Count +
                 " UdonSharpBehaviour types in loaded assemblies");
        return map;
    }

    private GameObject CreateChild(GameObject parent, string name)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent.transform);
        child.transform.localPosition = Vector3.zero;
        child.transform.localRotation = Quaternion.identity;
        child.transform.localScale = Vector3.one;
        Undo.RegisterCreatedObjectUndo(child, "Create " + name);
        return child;
    }

    private void WireWaypoints(GameObject root, GameObject waypointsParent)
    {
        // Find IdleWaypoints component and set _waypoints array
        UdonSharpBehaviour[] behaviours =
            root.GetComponentsInChildren<UdonSharpBehaviour>(true);

        foreach (UdonSharpBehaviour usb in behaviours)
        {
            if (usb.GetType().Name != "IdleWaypoints") continue;

            SerializedObject so = new SerializedObject(usb);
            SerializedProperty prop = so.FindProperty("_waypoints");
            if (prop == null) break;

            int count = waypointsParent.transform.childCount;
            prop.arraySize = count;
            for (int i = 0; i < count; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue =
                    waypointsParent.transform.GetChild(i);
            }
            so.ApplyModifiedProperties();
            _log.Add("[WIRE] IdleWaypoints._waypoints (" + count + " transforms)");
            break;
        }
    }

    private void WireParticlesAndVisuals(GameObject root,
        GameObject emotionParticles, GameObject dreamParticles,
        GameObject giftParticles, GameObject feRing)
    {
        UdonSharpBehaviour[] behaviours =
            root.GetComponentsInChildren<UdonSharpBehaviour>(true);

        foreach (UdonSharpBehaviour usb in behaviours)
        {
            string typeName = usb.GetType().Name;
            SerializedObject so = new SerializedObject(usb);

            if (typeName == "QuantumDharmaNPC")
            {
                SetRef(so, "_emotionParticles",
                    emotionParticles.GetComponent<ParticleSystem>());
            }
            else if (typeName == "DreamState")
            {
                SetRef(so, "_dreamParticles",
                    dreamParticles.GetComponent<ParticleSystem>());
            }
            else if (typeName == "GiftReceiver")
            {
                SetRef(so, "_giftBurstParticles",
                    giftParticles.GetComponent<ParticleSystem>());
            }
            else if (typeName == "FreeEnergyVisualizer")
            {
                SetRef(so, "_lineRenderer",
                    feRing.GetComponent<LineRenderer>());
            }
        }
    }

    private void CreateDebugUI(GameObject root)
    {
        // Create world-space Canvas
        GameObject canvasObj = CreateChild(root, "DebugCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasObj.AddComponent<CanvasScaler>();

        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(400, 600);
        canvasRect.localScale = Vector3.one * 0.002f;
        canvasRect.localPosition = new Vector3(0f, 2.5f, 0f);

        // Create text labels for DebugOverlay
        string[] labelNames = new string[]
        {
            "_stateLabel", "_freeEnergyLabel", "_trustLabel",
            "_radiusLabel", "_detailsLabel"
        };
        string[] displayNames = new string[]
        {
            "State", "Free Energy", "Trust", "Radius", "Details"
        };

        Text[] texts = new Text[labelNames.Length];
        for (int i = 0; i < labelNames.Length; i++)
        {
            GameObject textObj = CreateChild(canvasObj, displayNames[i]);
            RectTransform rt = textObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -i * 30f);
            rt.sizeDelta = new Vector2(0f, 30f);

            Text text = textObj.AddComponent<Text>();
            text.text = displayNames[i];
            text.fontSize = 14;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            texts[i] = text;
        }

        // Wire to DebugOverlay
        UdonSharpBehaviour[] behaviours =
            root.GetComponentsInChildren<UdonSharpBehaviour>(true);
        foreach (UdonSharpBehaviour usb in behaviours)
        {
            if (usb.GetType().Name != "DebugOverlay") continue;

            SerializedObject so = new SerializedObject(usb);
            for (int i = 0; i < labelNames.Length; i++)
            {
                SetRef(so, labelNames[i], texts[i]);
            }
            break;
        }

        // Utterance text for QuantumDharmaNPC
        GameObject utteranceObj = CreateChild(canvasObj, "UtteranceText");
        RectTransform urt = utteranceObj.AddComponent<RectTransform>();
        urt.anchorMin = new Vector2(0f, 0f);
        urt.anchorMax = new Vector2(1f, 0.3f);
        urt.offsetMin = Vector2.zero;
        urt.offsetMax = Vector2.zero;

        Text utteranceText = utteranceObj.AddComponent<Text>();
        utteranceText.text = "";
        utteranceText.fontSize = 18;
        utteranceText.color = new Color(1f, 1f, 0.8f);
        utteranceText.alignment = TextAnchor.UpperCenter;

        foreach (UdonSharpBehaviour usb in behaviours)
        {
            if (usb.GetType().Name != "QuantumDharmaNPC") continue;

            SerializedObject so = new SerializedObject(usb);
            SetRef(so, "_utteranceText", utteranceText);
            break;
        }

        _log.Add("[CHILD] DebugCanvas (World Space, 6 Text labels)");
    }

    private void SetRef(SerializedObject so, string propName,
        UnityEngine.Object value)
    {
        SerializedProperty prop = so.FindProperty(propName);
        if (prop != null)
        {
            prop.objectReferenceValue = value;
            so.ApplyModifiedProperties();
            _log.Add("[WIRE] " + so.targetObject.GetType().Name + "." +
                     propName + " -> " + value.GetType().Name);
        }
    }

    /// <summary>
    /// Inline auto-wire logic (same as QuantumDharmaAutoWirer but non-interactive).
    /// </summary>
    private int RunAutoWire(GameObject root)
    {
        UdonSharpBehaviour[] allBehaviours =
            root.GetComponentsInChildren<UdonSharpBehaviour>(true);

        // Build type -> component map
        Dictionary<Type, Component> typeMap = new Dictionary<Type, Component>();
        foreach (UdonSharpBehaviour usb in allBehaviours)
        {
            Type t = usb.GetType();
            if (!typeMap.ContainsKey(t))
            {
                typeMap[t] = usb;
            }
        }

        // Also add Unity components
        AddUnityComp<Animator>(root, typeMap);
        AddUnityComp<AudioSource>(root, typeMap);

        int wiredCount = 0;

        foreach (UdonSharpBehaviour usb in allBehaviours)
        {
            Type compType = usb.GetType();
            SerializedObject so = new SerializedObject(usb);

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
                if (!typeof(Component).IsAssignableFrom(fieldType))
                {
                    continue;
                }

                // Skip peer/external fields
                if (IsPeerField(compType.Name, field.Name))
                {
                    continue;
                }

                SerializedProperty prop = so.FindProperty(field.Name);
                if (prop == null) continue;

                // Skip already assigned
                if (prop.objectReferenceValue != null) continue;

                // Try to resolve
                if (typeMap.ContainsKey(fieldType))
                {
                    prop.objectReferenceValue = typeMap[fieldType];
                    so.ApplyModifiedProperties();
                    wiredCount++;
                }
                else
                {
                    // Check assignability
                    foreach (KeyValuePair<Type, Component> kvp in typeMap)
                    {
                        if (fieldType.IsAssignableFrom(kvp.Key))
                        {
                            prop.objectReferenceValue = kvp.Value;
                            so.ApplyModifiedProperties();
                            wiredCount++;
                            break;
                        }
                    }
                }
            }
        }

        return wiredCount;
    }

    private void AddUnityComp<T>(GameObject root,
        Dictionary<Type, Component> map) where T : Component
    {
        T comp = root.GetComponentInChildren<T>(true);
        if (comp != null && !map.ContainsKey(typeof(T)))
        {
            map[typeof(T)] = comp;
        }
    }

    private bool IsPeerField(string className, string fieldName)
    {
        if (className == "CollectiveMemory" &&
            (fieldName == "_peerMemory0" || fieldName == "_peerMemory1" ||
             fieldName == "_peerMemory2" || fieldName == "_peerMemory3"))
            return true;

        if (className == "MultiNPCRelay" &&
            (fieldName == "_peer0" || fieldName == "_peer1" ||
             fieldName == "_peer2" || fieldName == "_peer3"))
            return true;

        if (className == "PersonalityInstaller" && fieldName == "_avatarModels")
            return true;

        if (className == "IdleWaypoints" && fieldName == "_waypoints")
            return true;

        if (className == "TrustVisualizer" && fieldName == "_renderers")
            return true;

        return false;
    }
}
