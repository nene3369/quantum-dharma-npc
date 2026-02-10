using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
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
    private bool _generateAnimator = true;
    private bool _autoMapBlendShapes = true;
    private bool _createArchetypeUI = true;
    private bool _runValidation = true;
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
        _generateAnimator = EditorGUILayout.Toggle("Generate Animator Controller", _generateAnimator);
        _autoMapBlendShapes = EditorGUILayout.Toggle("Auto-Map BlendShapes", _autoMapBlendShapes);
        _createArchetypeUI = EditorGUILayout.Toggle("Create Archetype Selection UI", _createArchetypeUI);
        _runValidation = EditorGUILayout.Toggle("Run Validation After", _runValidation);

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

        // ── 6b. Archetype Selection UI ──
        if (_createArchetypeUI)
        {
            CreateArchetypeSelectionUI(root, typeMap);
        }

        // ── 7. Auto-wire all component cross-references ──
        if (_autoWire)
        {
            _log.Add("---");
            _log.Add("[AUTO-WIRE] Running auto-wirer...");
            int wired = RunAutoWire(root);
            _log.Add("[AUTO-WIRE] " + wired + " references connected");
        }

        // ── 8. Generate Animator Controller ──
        if (_generateAnimator)
        {
            _log.Add("---");
            _log.Add("[ANIMATOR] Generating AnimatorController...");
            string controllerPath =
                "Assets/QuantumDharma/Animations/" + _npcName + ".controller";
            RuntimeAnimatorController rac =
                QuantumDharmaAnimatorBuilder.GenerateDefaultController(controllerPath);
            if (rac != null)
            {
                animator.runtimeAnimatorController = rac;
                _log.Add("[ANIMATOR] Created at: " + controllerPath);
                _log.Add("[ANIMATOR] Assigned to Animator component");
            }
        }

        // ── 9. Auto-map blend shapes ──
        if (_autoMapBlendShapes)
        {
            _log.Add("---");
            _log.Add("[BLENDSHAPE] Scanning for blend shapes...");
            int mapped = QuantumDharmaAutoWirer.AutoMapBlendShapes(root, _log);
            _log.Add("[BLENDSHAPE] " + mapped + " indices mapped");
        }

        // ── 10. Select the created NPC ──
        Selection.activeGameObject = root;
        Undo.CollapseUndoOperations(undoGroup);
        Undo.SetCurrentGroupName("Create Quantum Dharma NPC");

        // ── 11. Run validation ──
        if (_runValidation)
        {
            _log.Add("---");
            _log.Add("[VALIDATE] Running setup validation...");
            int errors = 0;
            int warnings = 0;
            RunInlineValidation(root, _log, ref errors, ref warnings);
            _log.Add("[VALIDATE] " + errors + " errors, " + warnings + " warnings");
        }

        _log.Add("===");
        _log.Add("[DONE] NPC created and configured!");

        // Show remaining manual steps (if any)
        List<string> remaining = new List<string>();
        remaining.Add("  - Assign avatar model as child of " + _npcName);
        if (!_generateAnimator)
            remaining.Add("  - Create and assign Animator Controller");
        if (!_autoMapBlendShapes)
            remaining.Add("  - Set blend shape indices on FacialExpressionController");
        remaining.Add("  - Assign AnimationClips (idle, walk, gestures)");
        remaining.Add("  - Assign avatar models to PersonalityInstaller._avatarModels[] for switching");

        if (remaining.Count > 0)
        {
            _log.Add("Remaining manual steps:");
            foreach (string step in remaining)
                _log.Add(step);
        }

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

    // ================================================================
    // Archetype Selection UI
    // ================================================================

    private void CreateArchetypeSelectionUI(GameObject root,
        Dictionary<string, Type> typeMap)
    {
        _log.Add("---");
        _log.Add("[ARCHETYPE-UI] Creating selection panel...");

        // ── Canvas object (separate from Debug UI) ──
        GameObject canvasObj = CreateChild(root, "ArchetypeSelectionCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(320, 420);
        canvasRect.localScale = Vector3.one * 0.002f;
        canvasRect.localPosition = new Vector3(-0.8f, 1.5f, 0f);

        // Collider for VRChat Interact
        BoxCollider col = canvasObj.AddComponent<BoxCollider>();
        col.size = new Vector3(320f, 420f, 1f);
        col.center = Vector3.zero;

        // ── Add ArchetypeSelectionUI component ──
        Component archetypeUI = null;
        if (typeMap.ContainsKey("ArchetypeSelectionUI"))
        {
            archetypeUI = canvasObj.AddComponent(typeMap["ArchetypeSelectionUI"]);
            _log.Add("[ARCHETYPE-UI] Added ArchetypeSelectionUI component");
        }
        else
        {
            _log.Add("[WARN] ArchetypeSelectionUI type not found — " +
                "buttons will need manual wiring");
        }

        // ── Panel background ──
        GameObject panel = CreateChild(canvasObj, "Panel");
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image panelBg = panel.AddComponent<Image>();
        panelBg.color = new Color(0.08f, 0.08f, 0.15f, 0.9f);

        // ── Title text ──
        GameObject titleObj = CreateChild(panel, "TitleText");
        RectTransform titleRect = titleObj.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -8f);
        titleRect.sizeDelta = new Vector2(0f, 40f);

        Text titleText = titleObj.AddComponent<Text>();
        titleText.text = "性格を選択 / Select Personality";
        titleText.fontSize = 20;
        titleText.fontStyle = FontStyle.Bold;
        titleText.color = new Color(0.9f, 0.9f, 1f);
        titleText.alignment = TextAnchor.MiddleCenter;

        // ── Archetype buttons ──
        string[] methodNames = new string[]
        {
            "OnSelectGentleMonk",
            "OnSelectCuriousChild",
            "OnSelectShyGuardian",
            "OnSelectWarmElder",
            "OnSelectSilentSage"
        };

        string[] labelTexts = new string[]
        {
            "穏やかな僧 / Gentle Monk",
            "好奇心旺盛な子供 / Curious Child",
            "内気な守護者 / Shy Guardian",
            "温かい長老 / Warm Elder",
            "沈黙の賢者 / Silent Sage"
        };

        string[] descTexts = new string[]
        {
            "静かで忍耐強く、沈黙を好む",
            "活発で好奇心に満ち、すぐに友達になる",
            "慎重で警戒心が強いが、信頼すると忠実",
            "寛大で友好的、深い知恵を持つ",
            "寡黙だが、語る時は意味深い"
        };

        float buttonStartY = -56f;
        float buttonHeight = 58f;
        float buttonSpacing = 4f;
        Image[] buttonImages = new Image[5];

        // Find UdonBehaviour on the canvas object for button wiring
        Component udonTarget = FindUdonBehaviour(canvasObj);

        for (int i = 0; i < 5; i++)
        {
            float yPos = buttonStartY - i * (buttonHeight + buttonSpacing);

            // Button container
            GameObject btnObj = CreateChild(panel, "Button_" + i);
            RectTransform btnRect = btnObj.AddComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(0f, 1f);
            btnRect.anchorMax = new Vector2(1f, 1f);
            btnRect.pivot = new Vector2(0.5f, 1f);
            btnRect.anchoredPosition = new Vector2(0f, yPos);
            btnRect.sizeDelta = new Vector2(-16f, buttonHeight);

            Image btnBg = btnObj.AddComponent<Image>();
            btnBg.color = new Color(0.15f, 0.15f, 0.25f, 0.85f);
            buttonImages[i] = btnBg;

            Button btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = btnBg;

            // Wire Button.OnClick → UdonBehaviour.SendCustomEvent
            if (udonTarget != null)
            {
                WireButtonOnClick(btn, udonTarget, methodNames[i]);
                _log.Add("[WIRE] Button_" + i + ".OnClick -> " + methodNames[i]);
            }

            // Label text
            GameObject labelObj = CreateChild(btnObj, "Label");
            RectTransform labelRect = labelObj.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0.4f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(10f, 0f);
            labelRect.offsetMax = new Vector2(-10f, -4f);

            Text label = labelObj.AddComponent<Text>();
            label.text = labelTexts[i];
            label.fontSize = 14;
            label.fontStyle = FontStyle.Bold;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleLeft;

            // Description text
            GameObject descObj = CreateChild(btnObj, "Desc");
            RectTransform descRect = descObj.AddComponent<RectTransform>();
            descRect.anchorMin = new Vector2(0f, 0f);
            descRect.anchorMax = new Vector2(1f, 0.4f);
            descRect.offsetMin = new Vector2(10f, 2f);
            descRect.offsetMax = new Vector2(-10f, 0f);

            Text desc = descObj.AddComponent<Text>();
            desc.text = descTexts[i];
            desc.fontSize = 11;
            desc.color = new Color(0.7f, 0.7f, 0.8f);
            desc.alignment = TextAnchor.MiddleLeft;
        }

        // ── Status text ──
        GameObject statusObj = CreateChild(panel, "StatusText");
        RectTransform statusRect = statusObj.AddComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0f, 0f);
        statusRect.anchorMax = new Vector2(1f, 0f);
        statusRect.pivot = new Vector2(0.5f, 0f);
        statusRect.anchoredPosition = new Vector2(0f, 8f);
        statusRect.sizeDelta = new Vector2(0f, 30f);

        Text statusText = statusObj.AddComponent<Text>();
        statusText.text = "未選択 / Not Selected";
        statusText.fontSize = 13;
        statusText.color = new Color(0.8f, 0.85f, 1f);
        statusText.alignment = TextAnchor.MiddleCenter;

        // ── Wire ArchetypeSelectionUI fields ──
        if (archetypeUI != null)
        {
            SerializedObject so = new SerializedObject(archetypeUI);

            // _panel → Panel
            SerializedProperty panelProp = so.FindProperty("_panel");
            if (panelProp != null) panelProp.objectReferenceValue = panel;

            // _statusText → StatusText
            SerializedProperty statusProp = so.FindProperty("_statusText");
            if (statusProp != null) statusProp.objectReferenceValue = statusText;

            // _buttonImages → Image[]
            SerializedProperty btnImgProp = so.FindProperty("_buttonImages");
            if (btnImgProp != null)
            {
                btnImgProp.arraySize = 5;
                for (int i = 0; i < 5; i++)
                {
                    btnImgProp.GetArrayElementAtIndex(i).objectReferenceValue =
                        buttonImages[i];
                }
            }

            // _installer → PersonalityInstaller (from root)
            UdonSharpBehaviour[] rootBehaviours =
                root.GetComponents<UdonSharpBehaviour>();
            foreach (UdonSharpBehaviour usb in rootBehaviours)
            {
                if (usb.GetType().Name == "PersonalityInstaller")
                {
                    SerializedProperty instProp = so.FindProperty("_installer");
                    if (instProp != null) instProp.objectReferenceValue = usb;
                    _log.Add("[WIRE] ArchetypeSelectionUI._installer -> " +
                        "PersonalityInstaller");
                    break;
                }
            }

            // _preset → PersonalityPreset (from root)
            foreach (UdonSharpBehaviour usb in rootBehaviours)
            {
                if (usb.GetType().Name == "PersonalityPreset")
                {
                    SerializedProperty presetProp = so.FindProperty("_preset");
                    if (presetProp != null) presetProp.objectReferenceValue = usb;
                    _log.Add("[WIRE] ArchetypeSelectionUI._preset -> " +
                        "PersonalityPreset");
                    break;
                }
            }

            so.ApplyModifiedProperties();
        }

        _log.Add("[ARCHETYPE-UI] Selection panel created (" +
            "5 buttons, status text, auto-wired)");
    }

    /// <summary>
    /// Finds the UdonBehaviour component on a GameObject.
    /// In UdonSharp, each UdonSharpBehaviour has a backing UdonBehaviour.
    /// </summary>
    private Component FindUdonBehaviour(GameObject go)
    {
        Component[] allComps = go.GetComponents<Component>();
        foreach (Component c in allComps)
        {
            if (c == null) continue;
            // Match by type name to avoid hard dependency on VRC.Udon assembly
            if (c.GetType().Name == "UdonBehaviour")
            {
                return c;
            }
        }
        return null;
    }

    /// <summary>
    /// Wires a Button.OnClick event to call SendCustomEvent on an UdonBehaviour.
    /// Uses SerializedProperty to manipulate persistent calls without needing
    /// direct reference to VRC.Udon types at compile time.
    /// </summary>
    private void WireButtonOnClick(Button button, Component udonTarget,
        string eventName)
    {
        SerializedObject btnSO = new SerializedObject(button);
        SerializedProperty onClick = btnSO.FindProperty("m_OnClick");
        if (onClick == null) return;

        SerializedProperty calls =
            onClick.FindPropertyRelative("m_PersistentCalls.m_Calls");
        if (calls == null) return;

        int index = calls.arraySize;
        calls.InsertArrayElementAtIndex(index);
        SerializedProperty call = calls.GetArrayElementAtIndex(index);

        call.FindPropertyRelative("m_Target").objectReferenceValue = udonTarget;
        call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue =
            udonTarget.GetType().AssemblyQualifiedName;
        call.FindPropertyRelative("m_MethodName").stringValue =
            "SendCustomEvent";
        // PersistentListenerMode.String = 5
        call.FindPropertyRelative("m_Mode").intValue = 5;
        call.FindPropertyRelative("m_Arguments")
            .FindPropertyRelative("m_StringArgument").stringValue = eventName;
        // UnityEventCallState.RuntimeOnly = 2
        call.FindPropertyRelative("m_CallState").intValue = 2;

        btnSO.ApplyModifiedProperties();
    }

    // ================================================================
    // Helpers
    // ================================================================

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

    /// <summary>
    /// Inline validation: checks component presence and null references.
    /// Lighter than QuantumDharmaValidator (no Animator/bone checks since
    /// those require avatar model which isn't present at creation time).
    /// </summary>
    private void RunInlineValidation(GameObject root, List<string> log,
        ref int errors, ref int warnings)
    {
        UdonSharpBehaviour[] behaviours =
            root.GetComponentsInChildren<UdonSharpBehaviour>(true);

        // Check component count
        HashSet<string> found = new HashSet<string>();
        foreach (UdonSharpBehaviour usb in behaviours)
        {
            found.Add(usb.GetType().Name);
        }

        int missing = 0;
        foreach (string name in ComponentNames)
        {
            if (!found.Contains(name))
            {
                log.Add("[V-ERR] Missing: " + name);
                missing++;
                errors++;
            }
        }

        if (missing == 0)
        {
            log.Add("[V-OK] All " + ComponentNames.Length + " components present");
        }

        // Check null references on critical components
        string[] criticalTypes = new string[]
        {
            "QuantumDharmaManager", "FreeEnergyCalculator", "BeliefState",
            "QuantumDharmaNPC", "PlayerSensor", "NPCMotor"
        };

        foreach (UdonSharpBehaviour usb in behaviours)
        {
            string typeName = usb.GetType().Name;
            bool isCritical = false;
            foreach (string ct in criticalTypes)
            {
                if (typeName == ct) { isCritical = true; break; }
            }
            if (!isCritical) continue;

            FieldInfo[] fields = usb.GetType().GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            foreach (FieldInfo field in fields)
            {
                if (field.GetCustomAttribute<SerializeField>() == null &&
                    !field.IsPublic)
                    continue;

                if (!typeof(Component).IsAssignableFrom(field.FieldType))
                    continue;

                if (IsPeerField(typeName, field.Name))
                    continue;

                object val = field.GetValue(usb);
                if (val == null || val.Equals(null))
                {
                    log.Add("[V-WARN] " + typeName + "." + field.Name + " is null");
                    warnings++;
                }
            }
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
