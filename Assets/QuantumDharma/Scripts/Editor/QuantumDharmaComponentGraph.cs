using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UdonSharp;

/// <summary>
/// Builds and validates the component dependency graph for a Quantum Dharma NPC.
///
/// Features:
///   - Detects circular references between components
///   - Identifies unintentionally null external references
///   - Generates a .dot file for Graphviz visualization
///   - Reports orphaned components (nothing references them)
///
/// Menu: Quantum Dharma > Component Dependency Graph
/// </summary>
public class QuantumDharmaComponentGraph : EditorWindow
{
    private GameObject _npcRoot;
    private Vector2 _scrollPos;
    private readonly List<string> _log = new List<string>();
    private readonly List<EdgeInfo> _edges = new List<EdgeInfo>();
    private int _cycleCount;
    private int _nullRefCount;
    private int _orphanCount;

    private struct EdgeInfo
    {
        public string from;
        public string to;
        public string fieldName;
    }

    [MenuItem("Quantum Dharma/Component Dependency Graph")]
    private static void ShowWindow()
    {
        GetWindow<QuantumDharmaComponentGraph>("Dependency Graph");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Component Dependency Graph", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Analyzes the NPC component reference graph. Detects cycles, orphaned "
            + "components, and generates Graphviz .dot output for visualization.",
            MessageType.Info);
        EditorGUILayout.Space();

        _npcRoot = (GameObject)EditorGUILayout.ObjectField("NPC Root", _npcRoot, typeof(GameObject), true);

        EditorGUILayout.Space();

        EditorGUI.BeginDisabledGroup(_npcRoot == null);
        if (GUILayout.Button("Analyze Graph", GUILayout.Height(25)))
        {
            _log.Clear();
            _edges.Clear();
            _cycleCount = 0;
            _nullRefCount = 0;
            _orphanCount = 0;
            AnalyzeGraph();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(_edges.Count == 0);
        if (GUILayout.Button("Copy .dot to Clipboard"))
        {
            GUIUtility.systemCopyBuffer = GenerateDot();
            Log("Graphviz .dot content copied to clipboard");
        }
        EditorGUI.EndDisabledGroup();

        // Summary
        if (_log.Count > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Edges: " + _edges.Count + " | Cycles: " + _cycleCount
                + " | Null refs: " + _nullRefCount + " | Orphans: " + _orphanCount,
                EditorStyles.boldLabel);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(300));
            foreach (string line in _log)
            {
                Color oldColor = GUI.color;
                if (line.StartsWith("CYCLE")) GUI.color = Color.red;
                else if (line.StartsWith("NULL")) GUI.color = Color.yellow;
                else if (line.StartsWith("ORPHAN")) GUI.color = new Color(1f, 0.6f, 0.2f);
                EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
                GUI.color = oldColor;
            }
            EditorGUILayout.EndScrollView();
        }
    }

    private void AnalyzeGraph()
    {
        if (_npcRoot == null) return;

        UdonSharpBehaviour[] components = _npcRoot.GetComponentsInChildren<UdonSharpBehaviour>(true);
        Log("Found " + components.Length + " UdonSharpBehaviour components");

        // Map component instances to their type names
        Dictionary<Object, string> instanceNames = new Dictionary<Object, string>();
        HashSet<string> allTypeNames = new HashSet<string>();
        HashSet<string> referencedTypes = new HashSet<string>();

        foreach (UdonSharpBehaviour usb in components)
        {
            string typeName = usb.GetType().Name;
            instanceNames[usb] = typeName;
            allTypeNames.Add(typeName);
        }

        // Build edges from SerializeField references
        foreach (UdonSharpBehaviour usb in components)
        {
            string fromType = usb.GetType().Name;
            System.Type type = usb.GetType();
            FieldInfo[] fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (FieldInfo field in fields)
            {
                object[] attrs = field.GetCustomAttributes(typeof(SerializeField), true);
                if (attrs.Length == 0) continue;

                // Only check UdonSharpBehaviour references
                if (!typeof(UdonSharpBehaviour).IsAssignableFrom(field.FieldType)) continue;

                object val = field.GetValue(usb);
                if (val == null || val.Equals(null))
                {
                    _nullRefCount++;
                    // Only report if the field type is in our component set
                    string expectedType = field.FieldType.Name;
                    if (allTypeNames.Contains(expectedType))
                    {
                        Log("NULL: " + fromType + "." + field.Name + " (" + expectedType + ") is null");
                    }
                    continue;
                }

                UdonSharpBehaviour target = val as UdonSharpBehaviour;
                if (target == null) continue;

                string toType = target.GetType().Name;
                referencedTypes.Add(toType);

                EdgeInfo edge = new EdgeInfo();
                edge.from = fromType;
                edge.to = toType;
                edge.fieldName = field.Name;
                _edges.Add(edge);
            }
        }

        Log("Built " + _edges.Count + " edges, " + _nullRefCount + " null references");

        // Detect cycles using DFS
        DetectCycles(allTypeNames);

        // Detect orphans (types not referenced by anything)
        foreach (string typeName in allTypeNames)
        {
            // Skip the Manager (it's the root, nothing references it except maybe debug)
            if (typeName == "QuantumDharmaManager") continue;

            if (!referencedTypes.Contains(typeName))
            {
                _orphanCount++;
                Log("ORPHAN: " + typeName + " is not referenced by any other component");
            }
        }

        Log("");
        Log("Analysis complete.");
    }

    private void DetectCycles(HashSet<string> allTypes)
    {
        // Build adjacency list
        Dictionary<string, HashSet<string>> adj = new Dictionary<string, HashSet<string>>();
        foreach (string t in allTypes) adj[t] = new HashSet<string>();
        foreach (EdgeInfo e in _edges)
        {
            if (adj.ContainsKey(e.from)) adj[e.from].Add(e.to);
        }

        HashSet<string> visited = new HashSet<string>();
        HashSet<string> onStack = new HashSet<string>();
        List<string> path = new List<string>();

        foreach (string node in allTypes)
        {
            if (!visited.Contains(node))
            {
                DFSCycle(node, adj, visited, onStack, path);
            }
        }
    }

    private void DFSCycle(string node, Dictionary<string, HashSet<string>> adj,
        HashSet<string> visited, HashSet<string> onStack, List<string> path)
    {
        visited.Add(node);
        onStack.Add(node);
        path.Add(node);

        if (adj.ContainsKey(node))
        {
            foreach (string neighbor in adj[node])
            {
                if (!visited.Contains(neighbor))
                {
                    DFSCycle(neighbor, adj, visited, onStack, path);
                }
                else if (onStack.Contains(neighbor))
                {
                    _cycleCount++;
                    // Build cycle path string
                    int start = path.IndexOf(neighbor);
                    string cyclePath = "";
                    for (int i = start; i < path.Count; i++)
                    {
                        if (cyclePath.Length > 0) cyclePath += " -> ";
                        cyclePath += path[i];
                    }
                    cyclePath += " -> " + neighbor;
                    Log("CYCLE: " + cyclePath);
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        onStack.Remove(node);
    }

    private string GenerateDot()
    {
        string dot = "digraph QuantumDharma {\n";
        dot += "  rankdir=LR;\n";
        dot += "  node [shape=box, style=filled, fillcolor=lightyellow];\n";
        dot += "\n";

        foreach (EdgeInfo e in _edges)
        {
            dot += "  \"" + e.from + "\" -> \"" + e.to + "\"";
            dot += " [label=\"" + e.fieldName + "\"];\n";
        }

        dot += "}\n";
        return dot;
    }

    private void Log(string msg)
    {
        _log.Add(msg);
    }
}
