using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class IFCImporterWindow : EditorWindow
{
    private string pythonPath = "python"; // Or full path e.g. C:/Python311/python.exe
    private string convertScriptPath = "";
    private string ifcFilePath = "";
    private string outputFolder = "Assets/ImportedModels";

    // Converter parameters matching convert.py
    private bool upAxisY = true; // Recommended for Unity
    private bool keepUnits = false;
    private bool noColor = false;
    private bool linearColor = false;
    private float minAlpha = 0.0f;
    private bool printReport = true;
    private bool autoInstantiate = true;

    [MenuItem("IFC Converter/Import IFC model")]
    public static void ShowWindow()
    {
        GetWindow<IFCImporterWindow>("IFC to Unity Importer");
    }

    private void OnEnable()
    {
        // Load saved paths from EditorPrefs
        pythonPath = EditorPrefs.GetString("IFC_PythonPath", "python");
        convertScriptPath = EditorPrefs.GetString("IFC_ScriptPath", "");
    }

    private void OnGUI()
    {
        GUILayout.Label("IFC to OBJ Converter Plugin", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Environment Settings
        GUILayout.Label("Environment Configuration", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        pythonPath = EditorGUILayout.TextField("Python Executable", pythonPath);
        if (GUILayout.Button("Browse", GUILayout.Width(60)))
        {
            string path = EditorUtility.OpenFilePanel("Select Python Executable", "", "exe");
            if (!string.IsNullOrEmpty(path)) pythonPath = path;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        convertScriptPath = EditorGUILayout.TextField("convert.py Path", convertScriptPath);
        if (GUILayout.Button("Browse", GUILayout.Width(60)))
        {
            string path = EditorUtility.OpenFilePanel("Select convert.py", "", "py");
            if (!string.IsNullOrEmpty(path)) convertScriptPath = path;
        }
        EditorGUILayout.EndHorizontal();

        if (GUI.changed)
        {
            EditorPrefs.SetString("IFC_PythonPath", pythonPath);
            EditorPrefs.SetString("IFC_ScriptPath", convertScriptPath);
        }

        EditorGUILayout.Space();
        GUILayout.Label("File Selection", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        ifcFilePath = EditorGUILayout.TextField("IFC File", ifcFilePath);
        if (GUILayout.Button("Browse", GUILayout.Width(60)))
        {
            string path = EditorUtility.OpenFilePanel("Select IFC File", "", "ifc");
            if (!string.IsNullOrEmpty(path)) ifcFilePath = path;
        }
        EditorGUILayout.EndHorizontal();

        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);

        EditorGUILayout.Space();
        GUILayout.Label("Conversion Options", EditorStyles.boldLabel);

        upAxisY = EditorGUILayout.Toggle("Rotate to Y-Up (Unity)", upAxisY);
        keepUnits = EditorGUILayout.Toggle("Keep Native Units", keepUnits);
        noColor = EditorGUILayout.Toggle("Disable Materials (.mtl)", noColor);
        linearColor = EditorGUILayout.Toggle("Convert Colors to Linear", linearColor);
        minAlpha = EditorGUILayout.Slider("Min Opacity Floor", minAlpha, 0.0f, 1.0f);
        printReport = EditorGUILayout.Toggle("Log Report to Console", printReport);
        autoInstantiate = EditorGUILayout.Toggle("Instantiate in Scene", autoInstantiate);

        EditorGUILayout.Space();

        GUI.enabled = File.Exists(ifcFilePath) && File.Exists(convertScriptPath);
        if (GUILayout.Button("Convert & Import IFC", GUILayout.Height(35)))
        {
            RunConversion();
        }
        GUI.enabled = true;
    }

    private void RunConversion()
    {
        string projectPath = Application.dataPath; // <Project>/Assets
        string absoluteOutRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", outputFolder));

        Directory.CreateDirectory(absoluteOutRoot);

        // Build command line arguments for convert.py
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(ifcFilePath);
        string expectedObjFolder = Path.Combine(absoluteOutRoot, fileNameWithoutExt);
        string expectedObjPath = Path.Combine(expectedObjFolder, fileNameWithoutExt + ".obj");

        string arguments = $"\"{convertScriptPath}\" \"{ifcFilePath}\" \"{expectedObjPath}\"";

        if (upAxisY) arguments += " --up-axis y";
        if (keepUnits) arguments += " --keep-units";
        if (noColor) arguments += " --no-colour";
        if (linearColor) arguments += " --linear";
        if (printReport) arguments += " --report";
        if (minAlpha > 0.0f) arguments += $" --min-alpha {minAlpha}";
        arguments += $" --out-root \"{absoluteOutRoot}\"";

        // Find the folder containing convert.py (the 'conversion' folder)
        string scriptDirectory = Path.GetDirectoryName(convertScriptPath);
        // Step one level up to the parent folder (the 'ifc-converter' folder)
        string parentDirectory = Path.GetDirectoryName(scriptDirectory);

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = pythonPath,
            Arguments = arguments,
            WorkingDirectory = parentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (startInfo.EnvironmentVariables.ContainsKey("PYTHONPATH"))
        {
            startInfo.EnvironmentVariables["PYTHONPATH"] = parentDirectory + Path.PathSeparator + startInfo.EnvironmentVariables["PYTHONPATH"];
        }
        else
        {
            startInfo.EnvironmentVariables.Add("PYTHONPATH", parentDirectory);
        }

        EditorUtility.DisplayProgressBar("IFC Conversion", "Running Python IFC conversion descent...", 0.3f);

        try
        {
            using (Process process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                EditorUtility.ClearProgressBar();

                if (process.ExitCode == 0)
                {
                    Debug.Log($"<b>[IFC Converter Success]</b>\n{output}");
                    ImportAndSetupMesh(expectedObjPath);
                }
                else
                {
                    Debug.LogError($"<b>[IFC Converter Failed]</b> Code {process.ExitCode}\nError:\n{error}\nOutput:\n{output}");
                }
            }
        }
        catch (Exception ex)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"Failed to launch Python process: {ex.Message}");
        }
    }

    private void ImportAndSetupMesh(string absoluteObjPath)
    {
        // Normalize slashes so Windows paths match Unity paths
        string normalizedAbsolute = absoluteObjPath.Replace('\\', '/');
        string normalizedDataPath = Application.dataPath.Replace('\\', '/');

        // Remove everything up to the "Assets" folder, then prepend "Assets"
        string relativePath = "Assets" + normalizedAbsolute.Replace(normalizedDataPath, "");

        // Tell Unity to scan the disk so it sees the new files
        AssetDatabase.Refresh();

        GameObject importedObj = AssetDatabase.LoadAssetAtPath<GameObject>(relativePath);
        if (importedObj != null)
        {
            Debug.Log($"Successfully imported asset into Unity: {relativePath}");

            if (autoInstantiate)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(importedObj);
                Selection.activeGameObject = instance;
                Undo.RegisterCreatedObjectUndo(instance, "Import IFC Mesh");
            }
        }
        else
        {
            Debug.LogWarning($"Conversion succeeded, but Unity could not load asset at path: {relativePath}. Check output directory structure.");
        }
    }
}