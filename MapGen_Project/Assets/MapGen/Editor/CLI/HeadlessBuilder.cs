using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using System.IO;
using System.Collections.Generic;
using MapGen.Core;

namespace MapGen.Editor.CLI
{
    public static class HeadlessBuilder
    {
        // Pre-registered scene path to avoid AssetDatabase GUID timing issues in BatchMode
        private const string BakeScenePath = "Assets/MapGen/Generated/RuntimeBakeScene.unity";
        [Serializable]
        public struct BuildReport
        {
            public string sourceName;
            public int width, height;
            public float metersPerPixel;
            public float wallHeight;
            public string stackPath;
            public string outDir; // Requested outDir
            public string scenePath; // Actual saved scene path
            public string status; // "pass", "warn", "fail"
            public List<string> errors;
            public List<string> warnings;
            
            // Counts
            public int walls, floors, water, glass;
            public int spawns, teleporters, jumpPads, pickups;
            
            // Timing
            public double loadMs, buildMs, saveMs;
            
            // Bundle
            public bool buildBundle;
            public string bundleTarget;
            public string bundleDir;
            public string bundleName;
            public string bundlePath;      // Unity-relative path
            public string bundleDiskPath;  // Absolute filesystem path
        }

        public static void Build()
        {
            var report = new BuildReport { 
                errors = new List<string>(), 
                warnings = new List<string>(),
                status = "fail"
            };
            
            var watch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // 1. Argument Parsing
                string stackPath = Arg("-stack");
                if (string.IsNullOrEmpty(stackPath)) stackPath = Arg("--stack"); 

                string outDir = Arg("-outDir");
                if (string.IsNullOrEmpty(outDir)) outDir = Arg("--outDir");
                if (string.IsNullOrEmpty(outDir)) outDir = "Assets/_Generated/Maps";

                string themePath = Arg("-themeAsset");
                if (string.IsNullOrEmpty(themePath)) themePath = Arg("--themeAsset");

                string vocabPath = Arg("-ubervocab");
                if (string.IsNullOrEmpty(vocabPath)) vocabPath = Arg("--ubervocab");
                
                string seedStr = Arg("-seed");
                if (string.IsNullOrEmpty(seedStr)) seedStr = Arg("--seed");
                if (!string.IsNullOrEmpty(seedStr) && int.TryParse(seedStr, out int seed)) {
                    UnityEngine.Random.InitState(seed);
                }

                report.stackPath = stackPath;
                report.outDir = outDir;

                if (string.IsNullOrEmpty(stackPath))
                {
                     Error(ref report, "Missing -stack argument.");
                     Exit(1); return; 
                }

                if (!File.Exists(stackPath))
                {
                    Error(ref report, $"Stack file not found: {stackPath}");
                    Exit(1); return;
                }

                // 2. Load Stack
                string json = File.ReadAllText(stackPath);
                var def = StackDefinition.FromJson(json);
                def.directory = Path.GetDirectoryName(stackPath);
                report.sourceName = def.sourceName;
                report.metersPerPixel = def.metersPerPixel;
                report.wallHeight = def.wallHeight;

                // 3. Load & Enforce Import Settings
                report.loadMs = watch.Elapsed.TotalMilliseconds;
                watch.Restart();

                // Create scene FIRST to avoid unloading textures later
                var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                def.Layers = new StackDefinition.StackLayerBundle();
                def.Layers.layout = LoadTexture(def, def.layoutPath, true); 
                def.Layers.flow = LoadTexture(def, def.flowPath, true);
                def.Layers.height = LoadTexture(def, def.heightPath, false); 
                // def.Layers.zone does not exist in StackDefinition, removed.
                def.Layers.theme = LoadTexture(def, def.themePath, true);
                def.Layers.lighting = LoadTexture(def, def.lightingPath, true);
                def.Layers.collision = LoadTexture(def, def.collisionPath, true);

                if (def.Layers.layout != null) {
                    report.width = def.Layers.layout.width;
                    report.height = def.Layers.layout.height;
                }

                // Load Vocab
                if (!string.IsNullOrEmpty(vocabPath) && File.Exists(vocabPath)) {
                    UberVocab.Load(vocabPath);
                }

                // Load Theme
                ThemeDefinition theme = null;
                if (!string.IsNullOrEmpty(themePath)) {
                    theme = AssetDatabase.LoadAssetAtPath<ThemeDefinition>(themePath);
                }
                
                // 4. Build
                // var newScene moved up
                var builder = new GreyboxBuilder();
                Console.WriteLine($"[HeadlessBuilder] Pre-Generate Check: Layout={def.Layers.layout != null} (Width={def.Layers.layout?.width})");
                var root = builder.Generate(def, theme);
                
                report.buildMs = watch.Elapsed.TotalMilliseconds;
                watch.Restart();

                // 5. Gather Stats & QC
                if (root != null)
                {
                    if (root.transform.Find("Walls")) report.walls = root.transform.Find("Walls").childCount;
                    if (root.transform.Find("Floors")) report.floors = root.transform.Find("Floors").childCount;
                    if (root.transform.Find("Water")) report.water = root.transform.Find("Water").childCount;
                    if (root.transform.Find("Glass")) report.glass = root.transform.Find("Glass").childCount;

                    // Component-based counting
                    report.spawns = root.GetComponentsInChildren<MapGenSpawnPoint>(true).Length;
                    report.teleporters = root.GetComponentsInChildren<MapGenTeleporterNode>(true).Length;

                    var tokens = root.GetComponentsInChildren<MapGenPlacedToken>(true);
                    foreach(var t in tokens)
                    {
                        if (t.Token == FlowToken.JumpPad) report.jumpPads++;
                        else if (t.Token.ToString().StartsWith("Pickup")) report.pickups++;
                    }

                    // QC Logic
                    if (report.spawns < 8) Error(ref report, $"Insufficient spawns: {report.spawns} (Min 8)");
                    if (report.teleporters % 2 != 0) Warn(ref report, $"Odd number of teleporters: {report.teleporters}");
                    
                    if (report.errors.Count > 0) report.status = "fail";
                    else if (report.warnings.Count > 0) report.status = "warn";
                    else report.status = "pass";

                    // 6. Save Scene Logic
                    // Use pre-registered scene path to avoid AssetDatabase GUID timing issues
                    string finalScenePath = BakeScenePath;
                    
                    // Ensure directory exists
                    string sceneDir = Path.GetDirectoryName(finalScenePath);
                    if (!Directory.Exists(sceneDir)) Directory.CreateDirectory(sceneDir);
                    
                    EditorSceneManager.SaveScene(newScene, finalScenePath);
                    AssetDatabase.SaveAssets();
                    report.scenePath = finalScenePath;
                    report.saveMs = watch.Elapsed.TotalMilliseconds;

                    // 6b. Build Bundle Logic
                    // Accept both -buildBundle/--buildBundle and -bundle/--bundle as aliases
                    string buildBundleStr = Arg("-buildBundle") ?? Arg("--buildBundle") ?? Arg("-bundle") ?? Arg("--bundle");
                    bool buildBundle = !string.IsNullOrEmpty(buildBundleStr) && buildBundleStr != "0" && buildBundleStr.ToLower() != "false";

                    if (buildBundle)
                    {
                        report.buildBundle = true;
                        
                        string bundleTargetStr = Arg("-bundleTarget") ?? Arg("--bundleTarget");
                        if (string.IsNullOrEmpty(bundleTargetStr)) bundleTargetStr = "StandaloneWindows64";
                        report.bundleTarget = bundleTargetStr;
                        
                        var target = ParseBuildTarget(bundleTargetStr);
                        
                        string bundleOutDir = Arg("-bundleOutDir") ?? Arg("--bundleOutDir");
                        
                        // Default logic - use Unity-relative paths for AssetDatabase
                        string unityBundleDir;
                        if (string.IsNullOrEmpty(bundleOutDir))
                        {
                            unityBundleDir = $"Assets/_Generated/Bundles/{def.sourceName}";
                        }
                        else 
                        {
                            // If user specified OutDir, check if inside project
                            string pRoot = Path.GetFullPath(Application.dataPath).Replace("/Assets", "").Replace("\\Assets", "");
                            string absBd = Path.GetFullPath(bundleOutDir);
                            if (absBd.StartsWith(pRoot, StringComparison.OrdinalIgnoreCase))
                            {
                                // Inside project
                                if (absBd.StartsWith(Path.GetFullPath(Application.dataPath), StringComparison.OrdinalIgnoreCase)) {
                                    unityBundleDir = "Assets" + absBd.Substring(Path.GetFullPath(Application.dataPath).Length).Replace("\\", "/");
                                } else {
                                     // Project root but not assets?
                                     unityBundleDir = $"Assets/_Generated/Bundles/{def.sourceName}";
                                }
                            }
                            else
                            {
                                Warn(ref report, "External bundleOutDir not supported securely. Using internal default.");
                                unityBundleDir = $"Assets/_Generated/Bundles/{def.sourceName}";
                            }
                        }
                        
                        // Create Dirs - use absolute path for filesystem operations
                        string absBundleDir = Path.GetFullPath(unityBundleDir);
                        if (!Directory.Exists(absBundleDir)) Directory.CreateDirectory(absBundleDir);
                        
                        string bundleName = $"map_{def.sourceName.ToLowerInvariant()}";
                        report.bundleName = bundleName;
                        report.bundleDir = unityBundleDir;
                        
                        // AssetBundleBuild - verify GUID exists (should always exist for pre-registered scene)
                        string sceneRelPath = finalScenePath.Replace("\\", "/"); 
                        
                        var guid = UnityEditor.AssetDatabase.AssetPathToGUID(sceneRelPath);
                        Console.WriteLine($"[HeadlessBuilder] Building bundle for: {sceneRelPath} (GUID: {guid})");
                        
                        if (string.IsNullOrEmpty(guid)) {
                            Error(ref report, $"BakeScenePath has no GUID. Ensure {BakeScenePath} is committed with .meta file.");
                        }
                        else
                        {
                            UnityEditor.AssetBundleBuild[] buildMap = new UnityEditor.AssetBundleBuild[1];
                            buildMap[0].assetBundleName = bundleName;
                            buildMap[0].assetNames = new string[] { sceneRelPath };
                            
                            var manifest = UnityEditor.BuildPipeline.BuildAssetBundles(
                                unityBundleDir, 
                                buildMap, 
                                UnityEditor.BuildAssetBundleOptions.ForceRebuildAssetBundle | UnityEditor.BuildAssetBundleOptions.ChunkBasedCompression, 
                                target
                            );
                            
                            if (manifest != null)
                            {
                                // Success - store both Unity path and absolute disk path
                                report.bundlePath = $"{unityBundleDir}/{bundleName}".Replace("\\", "/");
                                report.bundleDiskPath = Path.GetFullPath(report.bundlePath);
                                Console.WriteLine($"[HeadlessBuilder] Bundle built: {report.bundlePath}");
                                Console.WriteLine($"[HeadlessBuilder] Bundle disk path: {report.bundleDiskPath}");
                            }
                            else
                            {
                                Error(ref report, "BuildPipeline.BuildAssetBundles returned null.");
                            }
                        }
                        
                        // Recompute status after bundle build (bundle errors should fail the build)
                        if (report.errors.Count > 0) report.status = "fail";
                        else if (report.warnings.Count > 0) report.status = "warn";
                        else report.status = "pass";

                    }
                }
                else
                {
                    Error(ref report, "Greybox generation returned null root.");
                }
            }
            catch (Exception ex)
            {
                Error(ref report, $"Exception: {ex.Message}\n{ex.StackTrace}");
            }

            // 7. Write Report (To requested outDir)
            try
            {
                string rOutDir = Path.GetFullPath(report.outDir);
                if (!Directory.Exists(rOutDir)) Directory.CreateDirectory(rOutDir);
                string rPath = Path.Combine(rOutDir, "build_report.json");
                File.WriteAllText(rPath, JsonUtility.ToJson(report, true));
                Console.WriteLine($"[HeadlessBuilder] Report saved to {rPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Failed to save report: {ex.Message}");
            }

            int code = 0;
            if (report.status == "fail") code = 1;
            else if (report.status == "warn") code = 2;
            
            Console.WriteLine($"[HeadlessBuilder] Finished with status: {report.status} (Code {code})");
            Exit(code);
        }

        private static UnityEditor.BuildTarget ParseBuildTarget(string s)
        {
            try { return (UnityEditor.BuildTarget)Enum.Parse(typeof(UnityEditor.BuildTarget), s, true); }
            catch { return UnityEditor.BuildTarget.StandaloneWindows64; }
        }

        private static string Arg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == name && i + 1 < args.Length)
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        private static void Error(ref BuildReport r, string msg) { r.errors.Add(msg); Console.WriteLine($"[Error] {msg}"); }
        private static void Warn(ref BuildReport r, string msg) { r.warnings.Add(msg); Console.WriteLine($"[Warning] {msg}"); }

        private static void Exit(int code)
        {
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        private static Texture2D LoadTexture(StackDefinition def, string relPath, bool sRGB)
        {
            if (string.IsNullOrEmpty(relPath)) {
                Console.WriteLine($"[HeadlessBuilder] Skipping texture (path empty)");
                return null;
            }
            string absPath = Path.Combine(def.directory, relPath);
            bool exists = File.Exists(absPath);
            Console.WriteLine($"[HeadlessBuilder] LoadTexture: '{relPath}' -> '{absPath}' (Exists: {exists})");
            
            if (!exists) return null;

            string fullPath = Path.GetFullPath(absPath).Replace("\\", "/");
            string dataPath = Application.dataPath.Replace("\\", "/");

            // Enforce Import Settings ONLY if inside Project Assets folder
            if (fullPath.StartsWith(dataPath))
            {
                 // Check if it's strictly inside (starts with)
                 string localPath = "Assets" + fullPath.Substring(dataPath.Length);
                 
                 TextureImporter importer = AssetImporter.GetAtPath(localPath) as TextureImporter;
                 if (importer != null)
                 {
                    bool changed = false;
                    if (importer.isReadable != true) { importer.isReadable = true; changed = true; }
                    if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
                    if (importer.filterMode != FilterMode.Point) { importer.filterMode = FilterMode.Point; changed = true; }
                    if (importer.sRGBTexture != sRGB) { importer.sRGBTexture = sRGB; changed = true; }
                    
                    if (changed) {
                        importer.SaveAndReimport();
                        Console.WriteLine($"[HeadlessBuilder] Fixed settings for {localPath}");
                    }
                 }
            }
            else
            {
                Console.WriteLine($"[HeadlessBuilder] Texture outside Assets; importer settings not applied: {fullPath}");
            }

            byte[] bytes = File.ReadAllBytes(absPath);
            Texture2D tex = new Texture2D(2, 2);
            tex.LoadImage(bytes);
            return tex;
        }
    }
}
