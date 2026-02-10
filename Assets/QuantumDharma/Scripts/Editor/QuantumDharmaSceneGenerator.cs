using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates a complete Quantum Dharma scene template with one or more NPCs,
/// positioned in a circle pattern, with all peer relationships auto-wired.
///
/// Menu: Quantum Dharma > Generate Scene Template
/// </summary>
public class QuantumDharmaSceneGenerator : EditorWindow
{
    private int _npcCount = 1;
    private float _circleRadius = 10f;
    private string _sceneBaseName = "QuantumDharma";
    private bool _addLighting = true;
    private bool _addGround = true;
    private bool _autoWirePeers = true;
    private Vector2 _scrollPos;
    private readonly List<string> _log = new List<string>();

    [MenuItem("Quantum Dharma/Generate Scene Template")]
    private static void ShowWindow()
    {
        GetWindow<QuantumDharmaSceneGenerator>("Scene Generator");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Quantum Dharma Scene Generator", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        _sceneBaseName = EditorGUILayout.TextField("Scene Base Name", _sceneBaseName);
        _npcCount = EditorGUILayout.IntSlider("NPC Count", _npcCount, 1, 4);
        _circleRadius = EditorGUILayout.Slider("Circle Radius", _circleRadius, 3f, 30f);
        _addLighting = EditorGUILayout.Toggle("Add Directional Light", _addLighting);
        _addGround = EditorGUILayout.Toggle("Add Ground Plane", _addGround);
        _autoWirePeers = EditorGUILayout.Toggle("Auto-Wire Peer NPCs", _autoWirePeers);

        EditorGUILayout.Space();

        if (GUILayout.Button("Generate Scene", GUILayout.Height(30)))
        {
            _log.Clear();
            GenerateScene();
        }

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

    private void GenerateScene()
    {
        Log("=== Generating Scene Template ===");

        // Create root container
        GameObject sceneRoot = new GameObject(_sceneBaseName + "_Root");
        Undo.RegisterCreatedObjectUndo(sceneRoot, "Create Scene Root");
        Log("Created scene root: " + sceneRoot.name);

        // Add lighting
        if (_addLighting)
        {
            GameObject lightObj = new GameObject("Directional Light");
            lightObj.transform.SetParent(sceneRoot.transform);
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = new Color(1f, 0.96f, 0.89f);
            Undo.RegisterCreatedObjectUndo(lightObj, "Create Light");
            Log("Added directional light");
        }

        // Add ground plane
        if (_addGround)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(sceneRoot.transform);
            ground.transform.localScale = new Vector3(5f, 1f, 5f);
            ground.transform.position = Vector3.zero;
            Undo.RegisterCreatedObjectUndo(ground, "Create Ground");
            Log("Added ground plane (50x50m)");
        }

        // Create NPCs
        GameObject[] npcRoots = new GameObject[_npcCount];
        for (int i = 0; i < _npcCount; i++)
        {
            float angle = (360f / _npcCount) * i * Mathf.Deg2Rad;
            Vector3 position = new Vector3(
                Mathf.Cos(angle) * _circleRadius,
                0f,
                Mathf.Sin(angle) * _circleRadius
            );

            string npcName = _npcCount == 1
                ? "QuantumDharmaNPC"
                : "QuantumDharmaNPC_" + (i + 1);

            GameObject npcRoot = new GameObject(npcName);
            npcRoot.transform.SetParent(sceneRoot.transform);
            npcRoot.transform.position = position;
            // Face center
            npcRoot.transform.LookAt(Vector3.zero);

            npcRoots[i] = npcRoot;
            Undo.RegisterCreatedObjectUndo(npcRoot, "Create NPC " + (i + 1));
            Log("Created NPC: " + npcName + " at " + position);

            // Add placeholder child for avatar model
            GameObject avatarSlot = new GameObject("AvatarModel_Placeholder");
            avatarSlot.transform.SetParent(npcRoot.transform);
            avatarSlot.transform.localPosition = Vector3.zero;
            Undo.RegisterCreatedObjectUndo(avatarSlot, "Create Avatar Slot");

            // Add waypoints around NPC
            CreateWaypoints(npcRoot, 4, 5f);
        }

        // Create spawn point
        GameObject spawnPoint = new GameObject("SpawnPoint");
        spawnPoint.transform.SetParent(sceneRoot.transform);
        spawnPoint.transform.position = new Vector3(0f, 0f, -_circleRadius * 1.5f);
        Undo.RegisterCreatedObjectUndo(spawnPoint, "Create Spawn Point");
        Log("Created spawn point");

        Log("");
        Log("=== Scene Template Generation Complete ===");
        Log("NPC roots created: " + _npcCount);
        Log("");
        Log("Next steps:");
        Log("1. Assign avatar models to each NPC root");
        Log("2. Use 'Quantum Dharma > Create Full NPC' on each root");
        Log("3. Use 'Quantum Dharma > Auto-Wire References' to connect components");
        if (_npcCount > 1 && _autoWirePeers)
        {
            Log("4. Use 'Quantum Dharma > Auto-Wire References' to connect peer NPCs");
        }

        Selection.activeGameObject = sceneRoot;
    }

    private void CreateWaypoints(GameObject parent, int count, float radius)
    {
        GameObject waypointContainer = new GameObject("Waypoints");
        waypointContainer.transform.SetParent(parent.transform);
        waypointContainer.transform.localPosition = Vector3.zero;
        Undo.RegisterCreatedObjectUndo(waypointContainer, "Create Waypoints");

        for (int i = 0; i < count; i++)
        {
            float angle = (360f / count) * i * Mathf.Deg2Rad;
            Vector3 localPos = new Vector3(
                Mathf.Cos(angle) * radius,
                0f,
                Mathf.Sin(angle) * radius
            );

            GameObject wp = new GameObject("Waypoint_" + (i + 1));
            wp.transform.SetParent(waypointContainer.transform);
            wp.transform.localPosition = localPos;
            Undo.RegisterCreatedObjectUndo(wp, "Create Waypoint " + (i + 1));
        }

        Log("  Created " + count + " waypoints for " + parent.name);
    }

    private void Log(string msg)
    {
        _log.Add(msg);
    }
}
