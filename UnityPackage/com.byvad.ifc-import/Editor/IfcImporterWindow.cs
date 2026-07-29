using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Conversion.Unity;
using Debug = UnityEngine.Debug;

namespace Conversion.Unity.Editor
{
    /// <summary>
    /// Import an IFC file straight into the open scene, no Python, no ifcopenshell,
    /// no external process.
    /// <para>
    /// This replaces a workflow that shelled out to a Python conversion script:
    /// same button, same options, same "select a file and go" feel, but every
    /// dependency it needs ships inside this package. A teammate installing the
    /// package from git has everything required; nothing to install separately,
    /// nothing to get onto their PATH, nothing that only works on the machine that
    /// happens to have the right Python environment.
    /// </para>
    /// <para>
    /// This runs synchronously on the main thread rather than through
    /// <see cref="IfcRuntimeLoader"/>'s coroutine. Unity's coroutine scheduler does
    /// not tick outside Play Mode, so the runtime loader's async path is not
    /// available here — and does not need to be: an editor import is a one-shot
    /// action with no frame to protect, so blocking behind a progress bar (the same
    /// shape the old tool had while it waited on the Python process) is the right
    /// choice, not a compromise.
    /// </para>
    /// </summary>
    public sealed class IfcImporterWindow : EditorWindow
    {
        private string _ifcFilePath = "";

        private bool _colour = true;
        private bool _includeOpenings = true;
        private bool _splitForFlatShading = true;
        private float _minAlpha = 0.25f;
        private string _classFilter = "";

        [MenuItem("IFC/Import IFC Model...")]
        public static void ShowWindow()
        {
            var window = GetWindow<IfcImporterWindow>("IFC Importer");
            window.minSize = new Vector2(420, 260);
        }

        private void OnEnable()
        {
            _ifcFilePath = EditorPrefs.GetString("Ifc.Importer.LastFile", "");
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            GUILayout.Label("IFC Model Import", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            _ifcFilePath = EditorGUILayout.TextField("IFC File", _ifcFilePath);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string path = EditorUtility.OpenFilePanel("Select IFC File", "", "ifc");
                if (!string.IsNullOrEmpty(path))
                {
                    _ifcFilePath = path;
                    EditorPrefs.SetString("Ifc.Importer.LastFile", path);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            GUILayout.Label("Options", EditorStyles.boldLabel);

            _includeOpenings = EditorGUILayout.Toggle(
                new GUIContent("Cut Openings", "Subtract windows and doors from walls. " +
                    "Off leaves every opening filled in as solid material."),
                _includeOpenings);
            _colour = EditorGUILayout.Toggle(
                new GUIContent("Apply Materials", "Resolve IFC surface styles and materials into URP materials."),
                _colour);

            using (new EditorGUI.DisabledScope(!_colour))
            {
                _minAlpha = EditorGUILayout.Slider(
                    new GUIContent("Min Opacity", "Floor on glazing opacity. IFC glass is routinely " +
                        "authored fully transparent (alpha 0), which renders as invisible."),
                    _minAlpha, 0f, 1f);
            }

            _splitForFlatShading = EditorGUILayout.Toggle(
                new GUIContent("Flat Shading", "Split shared vertices so faces shade flat instead of " +
                    "smoothing across hard edges like wall corners."),
                _splitForFlatShading);

            _classFilter = EditorGUILayout.TextField(
                new GUIContent("Class Filter", "Optional. Comma-separated IFC class names, " +
                    "e.g. IfcWall,IfcWindow. Empty imports everything."),
                _classFilter);

            EditorGUILayout.Space();

            bool canImport = File.Exists(_ifcFilePath);
            using (new EditorGUI.DisabledScope(!canImport))
            {
                if (GUILayout.Button("Import IFC Model", GUILayout.Height(35)))
                {
                    RunImport();
                }
            }
            if (!string.IsNullOrEmpty(_ifcFilePath) && !canImport)
            {
                EditorGUILayout.HelpBox("File not found.", MessageType.Warning);
            }
        }

        private void RunImport()
        {
            var options = new IfcLoadOptions
            {
                Colour = _colour,
                IncludeOpenings = _includeOpenings,
                SplitForFlatShading = _splitForFlatShading,
                MinAlpha = _minAlpha,
            };

            if (!string.IsNullOrWhiteSpace(_classFilter))
            {
                var classes = new List<string>();
                foreach (string name in _classFilter.Split(','))
                {
                    string trimmed = name.Trim();
                    if (trimmed.Length > 0)
                    {
                        classes.Add(trimmed);
                    }
                }
                if (classes.Count > 0)
                {
                    options.Classes = classes;
                }
            }

            EditorUtility.DisplayProgressBar("IFC Import", "Parsing and building geometry...", 0.5f);
            try
            {
                IfcImportPipeline.LoadDefaultTables(options,
                    out Dictionary<string, string> schemaCache, out List<string> taxonomyDocuments);

                if (schemaCache.Count == 0)
                {
                    Debug.LogError(
                        "[IFC] No schema tables found under Resources/IfcSchemas. Check the package " +
                        "installed correctly and ifc2x3.txt / ifc4.txt are present.");
                    return;
                }

                PreparedImport prepared = IfcImportPipeline.Prepare(
                    _ifcFilePath, options, schemaCache, taxonomyDocuments, progress: null);

                string rootName = Path.GetFileNameWithoutExtension(_ifcFilePath);
                GameObject root = IfcImportPipeline.Instantiate(
                    prepared, options, rootName, out IfcSceneBuilder builder);

                Undo.RegisterCreatedObjectUndo(root, "Import IFC Model");
                Selection.activeGameObject = root;
                EditorGUIUtility.PingObject(root);

                Debug.Log($"<b>[IFC Import]</b> {builder.ObjectsCreated} nodes, " +
                          $"{builder.RenderersCreated} renderers, {builder.TrianglesCreated} triangles, " +
                          $"{prepared.OpeningsCut} openings cut — " +
                          $"parse {prepared.ParseMilliseconds} ms, mesh {prepared.MeshMilliseconds} ms.");
            }
            catch (Exception exception)
            {
                Debug.LogError($"<b>[IFC Import Failed]</b> {exception.Message}\n{exception.StackTrace}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
