using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Process = System.Diagnostics.Process;

// IMPORTANT: This script assumes you have "Build Profiles" enabled (Unity 6+)
// If using an older Unity version, the concept still works for custom BuildPlayerOptions, 
// but the 'BuildProfile' asset type may need adjustment or replacement with your custom ScriptableObject.

namespace RadioDecadance.Serialization.Tools.Editor
{
    public class BuilderWindow : EditorWindow
    {
        // --- CONFIGURATION ---
        private const string ProfileAssetFolder = "Assets/Settings/Build Profiles";
        private const string WindowTitle = "Build Window";
        private const string MenuPath = "Build/Build Window";
        private const string EditorPrefsSelectedProfileKey = "BuilderWindow_SelectedProfile";
        private const string EditorPrefsExeNameKey = "BuilderWindow_ExeName";
        private const string EditorPrefsWriteBuildReportKey = "BuilderWindow_WriteBuildReport";
        private const string EditorPrefsCopySteamAppIdKey = "BuilderWindow_CopySteamAppId";
        private const string DefaultExeName = "BomberCatsNS";

        private static Process runningProcess;
        // --- END CONFIGURATION ---

        // List to store discovered profile assets
        private BuildProfile[] discoveredProfiles;
        private string[] profileNames = Array.Empty<string>();
        private int selectedProfileIndex;
        private Vector2 scrollPos;
        private GUIContent refreshIcon;

        // Foldouts and play state
        private bool buildFoldout = true;
        private bool playFoldout = true;

        private static bool GetWriteBuildReportEnabled()
        {
            return EditorPrefs.GetBool(EditorPrefsWriteBuildReportKey, false);
        }

        private static void SetWriteBuildReportEnabled(bool enabled)
        {
            EditorPrefs.SetBool(EditorPrefsWriteBuildReportKey, enabled);
        }

        private static bool GetCopySteamAppIdEnabled()
        {
            return EditorPrefs.GetBool(EditorPrefsCopySteamAppIdKey, true);
        }

        private static void SetCopySteamAppIdEnabled(bool enabled)
        {
            EditorPrefs.SetBool(EditorPrefsCopySteamAppIdKey, enabled);
        }

        private void OnEnable()
        {
            // Style initialization has been moved to InitializeStyles() 
            // because EditorStyles is not guaranteed to be initialized in OnEnable().
            ScanForProfiles();
        }

        private void OnGUI()
        {
            InitializeStyles(); // Initialize styles here where GUI context is guaranteed

            EditorGUILayout.Space(5);

            if (discoveredProfiles.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    $"No build profile assets found in: {ProfileAssetFolder}. Please add assets like 'WinDebug.asset' here.",
                    MessageType.Warning);

                if (GUILayout.Button("Scan Now"))
                {
                    ScanForProfiles();
                }

                return;
            }

            // Profile dropdown + Make Active
            EditorGUILayout.LabelField("Profile", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                selectedProfileIndex = EditorGUILayout.Popup(selectedProfileIndex, profileNames, GUILayout.Height(20f));

                if (EditorGUI.EndChangeCheck())
                {
                    SaveSelectedProfile();
                }

                // Refresh icon button to rescan profiles
                GUILayout.Space(4);

                if (GUILayout.Button(refreshIcon, GUILayout.Width(24), GUILayout.Height(20)))
                {
                    SaveSelectedProfile();
                    ScanForProfiles();
                }

                // Determine active profile and enable/disable the button accordingly
                BuildProfile selectedProfile =
                    selectedProfileIndex >= 0 && selectedProfileIndex < discoveredProfiles.Length
                        ? discoveredProfiles[selectedProfileIndex]
                        : null;

                var activeProfile = BuildProfile.GetActiveBuildProfile();
                bool isAlreadyActive = selectedProfile != null && activeProfile == selectedProfile;

                EditorGUI.BeginDisabledGroup(isAlreadyActive || selectedProfile == null);

                if (GUILayout.Button("Make Active", GUILayout.Width(110), GUILayout.Height(20)))
                {
                    if (selectedProfile != null)
                    {
                        BuildProfile.SetActiveBuildProfile(selectedProfile);
                        Debug.Log($"Active BuildProfile set to: {selectedProfile.name}");
                    }
                }

                EditorGUI.EndDisabledGroup();
            }

            if (selectedProfileIndex < 0 || selectedProfileIndex >= discoveredProfiles.Length)
            {
                selectedProfileIndex = 0;
            }

            string selectedProfileName = profileNames.Length > 0 ? profileNames[selectedProfileIndex] : "";

            // Show current active profile and target path
            var currentActive = BuildProfile.GetActiveBuildProfile();
            string activeName = currentActive != null ? currentActive.name : "<none>";
            EditorGUILayout.LabelField($"Active: {activeName}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Target: Builds/{selectedProfileName}/", EditorStyles.miniLabel);

            EditorGUILayout.Space(8);

            // Build Foldout
            buildFoldout = EditorGUILayout.Foldout(buildFoldout, "Build", true, EditorStyles.foldoutHeader);

            if (buildFoldout)
            {
                // Executable name field (stored in EditorPrefs)
                EditorGUI.BeginChangeCheck();
                string currentExeName = GetSavedExecutableName();
                string newExeName = EditorGUILayout.TextField("Executable name (.exe)", currentExeName);

                if (EditorGUI.EndChangeCheck())
                {
                    // Sanitize: trim and prevent empty/invalid names
                    if (string.IsNullOrWhiteSpace(newExeName))
                    {
                        newExeName = DefaultExeName;
                    }

                    newExeName = string.Join("", newExeName.Split(Path.GetInvalidFileNameChars())).Trim();

                    if (string.IsNullOrEmpty(newExeName))
                    {
                        newExeName = DefaultExeName;
                    }

                    EditorPrefs.SetString(EditorPrefsExeNameKey, newExeName);
                }

                // Option: Write Build Report (EditorPrefs-backed, default OFF)
                bool writeReport = GetWriteBuildReportEnabled();
                EditorGUI.BeginChangeCheck();
                writeReport = EditorGUILayout.ToggleLeft(new GUIContent("Write Build Report", "Creates log file in build folder with report."), writeReport);
                if (EditorGUI.EndChangeCheck())
                {
                    SetWriteBuildReportEnabled(writeReport);
                }

                // Option: Copy steam_appid.txt (EditorPrefs-backed, default OFF)
                bool copySteamAppId = GetCopySteamAppIdEnabled();
                EditorGUI.BeginChangeCheck();
                copySteamAppId = EditorGUILayout.ToggleLeft(new GUIContent("Copy steam_appid.txt to build", "Required for testing local build with steam, but shouldn't be published in retail!"), copySteamAppId);
                if (EditorGUI.EndChangeCheck())
                {
                    SetCopySteamAppIdEnabled(copySteamAppId);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Build", GUILayout.Height(28)))
                    {
                        BuildProfile profile = discoveredProfiles[selectedProfileIndex];
                        SaveSelectedProfile();
                        PerformBuild(profile, selectedProfileName);
                    }

                    if (GUILayout.Button("Clean Build", GUILayout.Height(28)))
                    {
                        BuildProfile profile = discoveredProfiles[selectedProfileIndex];
                        SaveSelectedProfile();
                        CleanBuildFolder(selectedProfileName);
                        PerformBuild(profile, selectedProfileName);
                    }
                }
            }

            EditorGUILayout.Space(10);

            // Determine executable presence for Play category
            string exePath = GetExecutablePath(selectedProfileName);
            bool exeAvailable = !string.IsNullOrEmpty(exePath) && File.Exists(exePath);

            if (!exeAvailable)
            {
                EditorGUILayout.HelpBox("First you need to build the game for selected profile", MessageType.Info);
            }

            using (new EditorGUI.DisabledGroupScope(!exeAvailable))
            {
                playFoldout = EditorGUILayout.Foldout(playFoldout, "Play", true, EditorStyles.foldoutHeader);

                if (playFoldout)
                {
                    bool isRunning = runningProcess != null && !runningProcess.HasExited;

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUI.BeginDisabledGroup(isRunning);

                        {
                            Color prevColor = GUI.backgroundColor;
                            GUI.backgroundColor = Color.green;

                            if (GUILayout.Button("Play", GUILayout.Height(28)))
                            {
                                StartGame(exePath);
                            }

                            GUI.backgroundColor = prevColor;
                        }

                        EditorGUI.EndDisabledGroup();

                        EditorGUI.BeginDisabledGroup(!isRunning);

                        {
                            Color prevColor2 = GUI.backgroundColor;
                            GUI.backgroundColor = Color.red;

                            if (GUILayout.Button("Stop", GUILayout.Height(28)))
                            {
                                StopRunningProcess();
                            }

                            GUI.backgroundColor = prevColor2;
                        }

                        EditorGUI.EndDisabledGroup();
                    }
                }
            }
        }

        [MenuItem(MenuPath)]
        public static void ShowWindow()
        {
            GetWindow<BuilderWindow>(WindowTitle);
        }

        private static string GetSavedExecutableName()
        {
            string name = EditorPrefs.GetString(EditorPrefsExeNameKey, DefaultExeName);

            if (string.IsNullOrWhiteSpace(name))
            {
                name = DefaultExeName;
            }

            name = string.Join("", name.Split(Path.GetInvalidFileNameChars())).Trim();

            if (string.IsNullOrEmpty(name))
            {
                name = DefaultExeName;
            }

            return name;
        }

        private static string GetFullOutputPath(string profileName)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string relativePath = $"Builds/{profileName}";

            return Path.Combine(projectRoot, relativePath);
        }

        private static void CleanBuildFolder(string profileName)
        {
            string fullOutputPath = GetFullOutputPath(profileName);

            if (Directory.Exists(fullOutputPath))
            {
                try
                {
                    // Use Unity's FileUtil to ensure AssetDatabase is kept in sync
                    FileUtil.DeleteFileOrDirectory(fullOutputPath);
                    FileUtil.DeleteFileOrDirectory(fullOutputPath + ".meta");
                    Debug.Log($"Cleaned build folder: {fullOutputPath}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to clean build folder '{fullOutputPath}': {ex}");
                }
            }

            // Recreate the folder to ensure build has a valid target
            if (!Directory.Exists(fullOutputPath))
            {
                Directory.CreateDirectory(fullOutputPath);
            }
        }

        private static void PerformBuild(BuildProfile profile, string profileName)
        {
            // 1. Set active build profile 
            BuildProfile.SetActiveBuildProfile(profile);

            if (profile.scenes.Length == 0)
            {
                profile.scenes = EditorBuildSettings.scenes;
            }

            // 2. Define the mandatory build path
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string relativePath = $"Builds/{profileName}";
            string fullOutputPath = Path.Combine(projectRoot, relativePath);

            if (!Directory.Exists(fullOutputPath))
            {
                Directory.CreateDirectory(fullOutputPath);
            }

            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;

            string executableName = GetSavedExecutableName();
            string extension = GetBuildExtension(target);
            string finalPathWithExe = Path.Combine(fullOutputPath, executableName + extension);

            // 3. Setup up BuildPlayerWithProfileOptions
            var profileOptions = new BuildPlayerWithProfileOptions
            {
                buildProfile = profile,
                locationPathName = finalPathWithExe
            };

            Debug.Log($"--- Starting Build for Profile: {profileName} ---");
            Debug.Log($"Output Path: {fullOutputPath}");

            // 4. build addressables
            string addressablesOutputPath = null;
            try
            {
                AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
                addressablesOutputPath = TryGetAddressablesOutput(result);
                if (!string.IsNullOrEmpty(result.Error))
                {
                    Debug.LogError($"Addressables build Failed: {result.Error}");
            
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                throw;
            }

            // 5. Execute the build
            BuildReport report = BuildPipeline.BuildPlayer(profileOptions);

            // 6. Copy steam_appid.txt if enabled
            if (GetCopySteamAppIdEnabled())
            {
                CopySteamAppIdFile(fullOutputPath);
            }

            // 7. Optionally persist build report to file
            if (GetWriteBuildReportEnabled())
            {
                try
                {
                    string reportPath = WriteBuildReportToFile(report, profileName, finalPathWithExe, addressablesOutputPath);
                    Debug.Log($"Build report saved: {reportPath}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to save build report: {ex.Message}");
                }
            }
            else
            {
                Debug.Log("Build report generation is disabled (Build Window > Write Build Report)");
            }

            // BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);

            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"✅ Build Succeeded! Time: {report.summary.totalTime.ToString()}");
                // Open the file explorer and highlight the executable file
                EditorUtility.RevealInFinder(finalPathWithExe);
            }
            else
            {
                Debug.LogError(
                    $"❌ Build Failed for {profileName}. Status: {report.summary.result.ToString()}. Error: {report.SummarizeErrors()}");
            }
        }

        private static void CopySteamAppIdFile(string buildOutputPath)
        {
            try
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                string sourceFile = Path.Combine(projectRoot, "steam_appid.txt");

                if (!File.Exists(sourceFile))
                {
                    Debug.LogWarning($"steam_appid.txt not found in project root: {sourceFile}");
                    return;
                }

                string destinationFile = Path.Combine(buildOutputPath, "steam_appid.txt");
                File.Copy(sourceFile, destinationFile, true);
                Debug.Log($"✅ Copied steam_appid.txt to: {destinationFile}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to copy steam_appid.txt: {ex.Message}");
            }
        }

        private static string TryGetAddressablesOutput(AddressablesPlayerBuildResult result)
        {
            if (result == null)
                return null;
            try
            {
                // Unity Addressables API commonly provides OutputPath
                var outputPath = result.OutputPath;
                if (!string.IsNullOrEmpty(outputPath))
                    return outputPath;
            }
            catch
            {
                // Ignore if property missing in this version
            }
            return null;
        }

        private static string WriteBuildReportToFile(BuildReport report, string profileName, string finalExePath, string addressablesOutputPath)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));

            // Save report into the build output folder (same place as the built player)
            string reportsDir = null;
            try
            {
                if (!string.IsNullOrEmpty(finalExePath))
                {
                    // For executable targets
                    reportsDir = Path.GetDirectoryName(finalExePath);
                }

                if (string.IsNullOrEmpty(reportsDir))
                {
                    // summary.outputPath can be a file path or a directory (e.g., WebGL)
                    var outPath = report.summary.outputPath;
                    if (!string.IsNullOrEmpty(outPath))
                    {
                        reportsDir = Directory.Exists(outPath) ? outPath : Path.GetDirectoryName(outPath);
                    }
                }
            }
            catch { }

            if (string.IsNullOrEmpty(reportsDir))
            {
                // Fallback to project Builds folder root if we cannot infer better
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                reportsDir = Path.Combine(projectRoot, "Builds");
            }

            if (!Directory.Exists(reportsDir))
                Directory.CreateDirectory(reportsDir);

            var summary = report.summary;
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string platform = summary.platform.ToString();

            string safeProfile = string.IsNullOrWhiteSpace(profileName) ? "UnknownProfile" : profileName.Replace(' ', '_');
            string fileName = $"BuildReport_{timestamp}_{safeProfile}_{platform}.txt";
            string fullPath = Path.Combine(reportsDir, fileName);

            var sb = new StringBuilder(16 * 1024);
            sb.AppendLine("==== Unity Build Report ====");
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Profile: {profileName}");
            sb.AppendLine($"Platform: {platform}");
            sb.AppendLine($"Result: {summary.result}");
            sb.AppendLine($"Output Path: {summary.outputPath}");
            if (!string.IsNullOrEmpty(finalExePath))
                sb.AppendLine($"Executable: {finalExePath}");
            if (!string.IsNullOrEmpty(addressablesOutputPath))
                sb.AppendLine($"Addressables Output: {addressablesOutputPath}");
            sb.AppendLine($"Total Time: {summary.totalTime}");
            sb.AppendLine($"Total Size: {summary.totalSize / (1024f * 1024f):0.00} MB");
            sb.AppendLine($"Warnings: {summary.totalWarnings}, Errors: {summary.totalErrors}");
            sb.AppendLine();

            // Errors/Warnings summary if available
            try
            {
                var errors = report.SummarizeErrors();
                if (!string.IsNullOrEmpty(errors))
                {
                    sb.AppendLine("-- Errors Summary --");
                    sb.AppendLine(errors);
                    sb.AppendLine();
                }
            }
            catch { /* API differences */ }

            // List of built files
            try
            {
                var files = report.GetFiles();
                if (files != null && files.Length > 0)
                {
                    sb.AppendLine("-- Built Files --");
                    foreach (var f in files)
                    {
                        try
                        {
                            sb.AppendLine($"- {f.path} ({f.role})");
                        }
                        catch
                        {
                            sb.AppendLine($"- {f.path}");
                        }
                    }
                    sb.AppendLine();
                }
            }
            catch { /* API differences */ }

            // Output directory tree for quick inspection of produced artifacts
            try
            {
                string buildDir = null;
                try
                {
                    // For executable targets, use directory of the final exe; otherwise use summary.outputPath
                    if (!string.IsNullOrEmpty(finalExePath))
                        buildDir = Path.GetDirectoryName(finalExePath);
                    if (string.IsNullOrEmpty(buildDir))
                        buildDir = Path.GetDirectoryName(summary.outputPath);
                }
                catch { }

                if (!string.IsNullOrEmpty(buildDir) && Directory.Exists(buildDir))
                {
                    sb.AppendLine("-- Output Directory Contents --");
                    AppendDirectoryListing(sb, buildDir, 2);
                    sb.AppendLine();
                }

                if (!string.IsNullOrEmpty(addressablesOutputPath) && Directory.Exists(addressablesOutputPath))
                {
                    sb.AppendLine("-- Addressables Output Contents --");
                    AppendDirectoryListing(sb, addressablesOutputPath, 2);
                    sb.AppendLine();
                }
            }
            catch { /* ignore */ }

            // Steps
            try
            {
                if (report.steps != null && report.steps.Length > 0)
                {
                    sb.AppendLine("-- Steps --");
                    foreach (var s in report.steps)
                    {
                        sb.AppendLine($"Step: {s.name} ({s.duration})");
                        try
                        {
                            if (s.messages != null && s.messages.Length > 0)
                            {
                                foreach (var m in s.messages)
                                {
                                    sb.AppendLine($"   [{m.type}] {m.content}");
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { /* API differences */ }

            File.WriteAllText(fullPath, sb.ToString(), Encoding.UTF8);
            return fullPath;
        }

        private static void AppendDirectoryListing(StringBuilder sb, string rootPath, int indentSpaces)
        {
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return;
            try
            {
                AppendDirectoryListingInternal(sb, rootPath, indentSpaces, 0, 3);
            }
            catch { }
        }

        private static void AppendDirectoryListingInternal(StringBuilder sb, string dir, int indentSpaces, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;
            string indent = new string(' ', depth * indentSpaces);
            try
            {
                foreach (var d in Directory.GetDirectories(dir))
                {
                    sb.AppendLine($"{indent}[{Path.GetFileName(d)}]/");
                    AppendDirectoryListingInternal(sb, d, indentSpaces, depth + 1, maxDepth);
                }
                foreach (var f in Directory.GetFiles(dir))
                {
                    var fi = new FileInfo(f);
                    sb.AppendLine($"{indent}- {Path.GetFileName(f)} ({fi.Length / 1024f:0.0} KB)");
                }
            }
            catch { }
        }

        // Helper to get the required extension for different platforms
        private static string GetBuildExtension(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneWindows:
                    return ".exe";
                case BuildTarget.StandaloneOSX:
                    return ".app"; // For macOS, this returns the folder (MyApp.app)
                case BuildTarget.WebGL:
                    return ""; // WebGL builds to a folder
                case BuildTarget.Android:
                    return ".apk";
                default:
                    return "";
            }
        }

        private static string GetExecutablePath(string profileName)
        {
            if (string.IsNullOrEmpty(profileName))
            {
                return null;
            }

            string outputDir = GetFullOutputPath(profileName);
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            string ext = GetBuildExtension(target);

            if (string.IsNullOrEmpty(ext))
            {
                return null; // Non-executable platforms (e.g., WebGL)
            }

            string exeName = GetSavedExecutableName() + ext;

            return Path.Combine(outputDir, exeName);
        }

        private static void StartGame(string exePath)
        {
            try
            {
                if (runningProcess != null && !runningProcess.HasExited)
                {
                    Debug.Log("Game is already running.");

                    return;
                }

                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    Debug.LogWarning($"Executable not found at: {exePath}");

                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath),
                    UseShellExecute = true
                };

                runningProcess = Process.Start(startInfo);

                if (runningProcess != null)
                {
                    runningProcess.EnableRaisingEvents = true;
                    runningProcess.Exited += (s, e) => { runningProcess = null; };
                    Debug.Log($"Started game: {exePath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to start game '{exePath}': {ex}");
            }
        }

        private static void StopRunningProcess()
        {
            if (runningProcess == null)
            {
                return;
            }

            try
            {
                if (!runningProcess.HasExited)
                {
                    // Try graceful close first
                    try
                    {
                        runningProcess.CloseMainWindow();
                    }
                    catch
                    {
                        /* ignore */
                    }

                    // Give it a moment
                    Thread.Sleep(500);

                    if (!runningProcess.HasExited)
                    {
                        runningProcess.Kill();
                    }
                }

                runningProcess.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error stopping game process: {ex}");
            }
            finally
            {
                runningProcess = null;
            }
        }

        // Initializes GUIStyles only when the GUI context is available (first time OnGUI runs)
        private void InitializeStyles()
        {
            if (refreshIcon == null)
            {
                refreshIcon = EditorGUIUtility.IconContent("Refresh");

                if (refreshIcon != null)
                {
                    refreshIcon.tooltip = "Manual Rescan Profiles";
                }
                else
                {
                    refreshIcon = new GUIContent("⟲", "Manual Rescan Profiles");
                }
            }
        }

        // Scan the specified folder for asset files
        private void ScanForProfiles()
        {
            if (!Directory.Exists(ProfileAssetFolder))
            {
                Debug.LogError($"Build Profile folder not found: {ProfileAssetFolder}. Please create it.");
                discoveredProfiles = Array.Empty<BuildProfile>();
                profileNames = Array.Empty<string>();
                selectedProfileIndex = 0;

                return;
            }

            // Find all GUIDs of assets in the folder
            string[] guids = AssetDatabase.FindAssets("", new[] { ProfileAssetFolder });

            // Load the assets
            discoveredProfiles = guids
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .Where(path => path.EndsWith(".asset")) // Filter only asset files
                .Select(path => AssetDatabase.LoadAssetAtPath<BuildProfile>(path))
                .Where(obj => obj != null)
                .OrderBy(obj => obj.name)
                .ToArray();

            profileNames = discoveredProfiles.Select(p => p.name).ToArray();

            // Restore last selected profile from EditorPrefs
            string savedName = EditorPrefs.GetString(EditorPrefsSelectedProfileKey, string.Empty);

            if (!string.IsNullOrEmpty(savedName))
            {
                int idx = Array.FindIndex(profileNames, n => n == savedName);
                selectedProfileIndex = idx >= 0 ? idx : 0; // if missing pick first
            }
            else
            {
                selectedProfileIndex = 0;
            }
        }

        private void DrawProfileButton(BuildProfile profile)
        {
            /* Deprecated by dropdown UI */
        }

        private void SaveSelectedProfile()
        {
            if (profileNames != null && profileNames.Length > 0 && selectedProfileIndex >= 0 &&
                selectedProfileIndex < profileNames.Length)
            {
                EditorPrefs.SetString(EditorPrefsSelectedProfileKey, profileNames[selectedProfileIndex]);
            }
        }
    }
}