using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor.Build.Pipeline;
using MapGen.Core;

namespace MapGen.Editor.CLI
{
    public static class HeadlessBuilder
    {
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
            public string bundlePath;
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
                var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var builder = new GreyboxBuilder();
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
                    // Determine where to save the .unity file.
                    // If outDir is inside project (Assets/...), save there.
                    // If outDir is absolute external, save to Assets/_Generated/Maps fallback, and export report to external.
                    
                    string absOutDir = Path.GetFullPath(outDir);
                    string projectRoot = Path.GetFullPath(Application.dataPath).Replace("/Assets", "").Replace("\\", "/");
                    // Normalize absOutDir separators
                    absOutDir = absOutDir.Replace("\\", "/");

                    bool isInsideProject = absOutDir.StartsWith(projectRoot);
                    string finalScenePath;

                    if (isInsideProject)
                    {
                         // Calculate relative path
                         // e.g. C:/Proj/Assets/Out -> Assets/Out
                         string relPath = "Assets" + absOutDir.Substring(projectRoot.Length + "/Assets".Length); // or safer logic
                         // Better: substring from projectRoot length + 1
                         if (absOutDir.Length > projectRoot.Length) {
                            relPath = absOutDir.Substring(projectRoot.Length + 1);
                         } else {
                            relPath = "Assets";
                         }
                         
                         if (!Directory.Exists(relPath)) Directory.CreateDirectory(relPath);
                         finalScenePath = $"{relPath}/Map_{def.sourceName}.unity";
                    }
                    else
                    {
                        // External path -> Save to default internal path
                        string internalDir = "Assets/_Generated/Maps";
                        if (!Directory.Exists(internalDir)) Directory.CreateDirectory(internalDir);
                        finalScenePath = $"{internalDir}/Map_{def.sourceName}.unity";
                        Warn(ref report, $"Output directory is external. Scene saved internally at {finalScenePath}");
                    }
                    
                    EditorSceneManager.SaveScene(newScene, finalScenePath);
                    report.scenePath = finalScenePath;
                    report.saveMs = watch.Elapsed.TotalMilliseconds;

                    // 6b. Build Bundle Logic
                    string buildBundleStr = Arg("-buildBundle") ?? Arg("--buildBundle");
                    bool buildBundle = !string.IsNullOrEmpty(buildBundleStr) && buildBundleStr != "0" && buildBundleStr.ToLower() != "false";

                    if (buildBundle)
                    {
                        report.buildBundle = true;
                        
                        string bundleTargetStr = Arg("-bundleTarget") ?? Arg("--bundleTarget");
                        if (string.IsNullOrEmpty(bundleTargetStr)) bundleTargetStr = "StandaloneWindows64";
                        report.bundleTarget = bundleTargetStr;
                        
                        var target = ParseBuildTarget(bundleTargetStr);
                        
                        string bundleOutDir = Arg("-bundleOutDir") ?? Arg("--bundleOutDir");
                        
                        // Default logic
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
                        
                        // Create Dirs
                        if (!Directory.Exists(unityBundleDir)) Directory.CreateDirectory(unityBundleDir);
                        
                        string bundleName = $"map_{def.sourceName.ToLowerInvariant()}";
                        report.bundleName = bundleName;
                        report.bundleDir = unityBundleDir;
                        
                        // AssetBundleBuild
                        string sceneRelPath = finalScenePath.Replace("\\", "/"); 
                        // Ensure it starts with Assets
                        if (!sceneRelPath.StartsWith("Assets")) sceneRelPath = "Assets/" + sceneRelPath; // Robustness
                        
                        AssetBundleBuild[] buildMap = new AssetBundleBuild[1];
                        buildMap[0].assetBundleName = bundleName;
                        buildMap[0].assetNames = new string[] { sceneRelPath };
                        
                        var manifest = BuildPipeline.BuildAssetBundles(unityBundleDir, buildMap, BuildAssetBundleOptions.ChunkBasedCompression, target);
                        
                        if (manifest != null)
                        {
                            // Success
                            string diskPath = Path.Combine(unityBundleDir, bundleName);
                            report.bundlePath = diskPath;
                            Console.WriteLine($"[HeadlessBuilder] Bundle built: {diskPath}");
                        }
                        else
                        {
                             Error(ref report, "BuildPipeline.BuildAssetBundles returned null.");
                        }
                    }
                    
            // 7. Write Report (To requested outDir)
                    if (!Directory.Exists(absOutDir)) Directory.CreateDirectory(absOutDir);
                    string rPath = Path.Combine(absOutDir, "build_report.json");
                    File.WriteAllText(rPath, JsonUtility.ToJson(report, true));
                    
                    Console.WriteLine($"[HeadlessBuilder] Report saved to {rPath}");
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
            
            int code = 0;
            if (report.status == "fail") code = 1;
            else if (report.status == "warn") code = 2;
            
            Console.WriteLine($"[HeadlessBuilder] Finished with status: {report.status} (Code {code})");
            Exit(code);
        }

        private static BuildTarget ParseBuildTarget(string s)
        {
            try { return (BuildTarget)Enum.Parse(typeof(BuildTarget), s, true); }
            catch { return BuildTarget.StandaloneWindows64; }
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
            if (string.IsNullOrEmpty(relPath)) return null;
            string absPath = Path.Combine(def.directory, relPath);
            if (!File.Exists(absPath)) return null;

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

            byte[] bytes = File.ReadAllBytes(absPath);
            Texture2D tex = new Texture2D(2, 2);
            tex.LoadImage(bytes);
            return tex;
        }
    }
}
