import os
import sys
import json
import subprocess
import shutil
import platform
from pathlib import Path
from typing import Dict, Any, Optional, Tuple, List
import yaml

class HeadlessPipeline:
    def __init__(self):
        self.project_root = self._find_project_root()
        self.config = self._load_config()

    def _find_project_root(self) -> Path:
        """Find the root of the repo by looking for MapGen_Project or .git"""
        current = Path.cwd()
        for _ in range(10): # Max depth up
            if (current / "MapGen_Project").exists():
                return current
            if (current / ".git").exists():
                return current
            parent = current.parent
            if parent == current:
                break
            current = parent
        return Path.cwd() # Fallback

    def _load_config(self) -> Dict[str, Any]:
        """Load optional local config from DesktopAgent/config/local.json"""
        local_config = self.project_root / "DesktopAgent" / "config" / "local.json"
        if local_config.exists():
            try:
                with open(local_config, 'r') as f:
                    return json.load(f)
            except Exception:
                pass
        return {}

    def find_unity_exe(self) -> str:
        """Resolve Unity Executable path using priority: Env > Config > Default"""
        # 1. Env Var
        env_path = os.environ.get("UNITY_EXE")
        if env_path:
            # Robustness: strip quotes
            env_path = env_path.strip('"\'')
            if os.path.exists(env_path):
                return env_path
            print(f"[Agent] Warning: UNITY_EXE env var set but path not found by Python: {env_path}. proceeding anyway.")
            return env_path
        
        # 2. Config
        cfg_path = self.config.get("unity_path")
        if cfg_path:
            cfg_path = cfg_path.strip('"\'')
            if os.path.exists(cfg_path):
                return cfg_path
            print(f"[Agent] Warning: Config unity_path set but not found by Python: {cfg_path}. proceeding anyway.")
            return cfg_path
            
        # 3. Defaults
        system = platform.system()
        candidates = []
        if system == "Windows":
            candidates = [
                r"C:\Program Files\Unity\Hub\Editor\6000.0.24f1\Editor\Unity.exe",
                r"C:\Program Files\Unity\Hub\Editor\2022.3.0f1\Editor\Unity.exe"
            ]
        elif system == "Darwin": # Mac
            candidates = [
                 "/Applications/Unity/Hub/Editor/6000.0.24f1/Unity.app/Contents/MacOS/Unity",
            ]
        elif system == "Linux":
            candidates = [
                str(Path.home() / "Unity/Hub/Editor/6000.0.24f1/Editor/Unity")
            ]
            
        for c in candidates:
            if os.path.exists(c):
                return c
                
        raise FileNotFoundError(f"Unity Executable not found. Env: '{env_path}' (Exists: {os.path.exists(env_path) if env_path else 'N/A'}). Checked: {candidates}")

    def run_build(self, 
                  stack_path: str, 
                  out_dir: str, 
                  theme_asset: Optional[str] = None, 
                  ubervocab: Optional[str] = None, 
                  seed: Optional[int] = None,
                  use_ps1: bool = False,
                  unity_log: Optional[str] = None,
                  bundle: bool = False,
                  bundle_target: str = "StandaloneWindows64",
                  bundle_out_dir: Optional[str] = None) -> Dict[str, Any]:
        
        unity_exe = self.find_unity_exe()
        if unity_exe:
             unity_exe = os.path.normpath(unity_exe)
             
        # Paths
        scripts_dir = self.project_root / "MapGen_Project" / "scripts"
        
        # Robust out_dir normalization
        normalized_out_dir = out_dir
        if out_dir.replace("\\", "/").startswith("Assets/") or out_dir.replace("\\", "/").startswith("Assets\\"):
            # Keep Unity-relative
            normalized_out_dir = out_dir.replace("\\", "/")
            # For file system operations (logging/reporting), we need the absolute path
            fs_out_dir = self.project_root / "MapGen_Project" / normalized_out_dir
        else:
            # Check if relative or absolute
            p = Path(out_dir)
            if not p.is_absolute():
                fs_out_dir = (self.project_root / "MapGen_Project" / p).resolve()
            else:
                fs_out_dir = p
            normalized_out_dir = str(fs_out_dir)

        # Prepare Environment
        env = os.environ.copy()
        if unity_exe:
            env["UNITY_EXE"] = unity_exe
        
        # Select Script
        system = platform.system()
        cmd = []
        
        if system == "Windows":
            if use_ps1:
                script = scripts_dir / "gen_map.ps1"
                cmd = ["powershell", "-ExecutionPolicy", "Bypass", "-File", str(script)]
            else:
                script = scripts_dir / "gen_map.bat"
                # Usage of cmd /c is much more reliable for .bat files
                cmd = ["cmd.exe", "/c", str(script)]
        else:
            script = scripts_dir / "gen_map.sh"
            cmd = ["bash", str(script)]
            
        if not script.exists():
            return {"status": "fail", "error": f"Build script not found: {script}"}

        # Args
        cmd.extend(["-stack", stack_path])
        cmd.extend(["-outDir", normalized_out_dir])
        if theme_asset:
            cmd.extend(["-themeAsset", theme_asset])
        if ubervocab:
            cmd.extend(["-ubervocab", ubervocab])
        if seed is not None:
             cmd.extend(["-seed", str(seed)])

        # Bundle Args
        if bundle:
            cmd.extend(["-buildBundle", "1"])
            cmd.extend(["-bundleTarget", bundle_target])
            if bundle_out_dir:
                 cmd.extend(["-bundleOutDir", bundle_out_dir])

        # Determine log path
        # If logging to out_dir, use fs_out_dir
        if unity_log:
             log_file = unity_log
        else:
             log_file = os.path.join(fs_out_dir, "unity_build.log")
        
        # Create output directory if it doesn't exist
        if not os.path.exists(fs_out_dir):
            os.makedirs(fs_out_dir, exist_ok=True)

        print(f"[Agent] Executing: {' '.join(cmd)}")
        if unity_exe: print(f"[Agent] Unity: {unity_exe}")
        
        try:
            with open(log_file, "w") as logout:
                 # Log header
                 logout.write(f"CMD: {' '.join(cmd)}\n")
                 if unity_exe: logout.write(f"UNITY_EXE={unity_exe}\n\n")
                 logout.flush()
                 
                 process = subprocess.run(cmd, env=env, stdout=logout, stderr=subprocess.STDOUT)
                 
            # Note: Exit code might be 0, 1, or 2 from our script wrapper
            exit_code = process.returncode
            
            # Read Report
            report_path = fs_out_dir / "build_report.json"
            
            if report_path.exists():
                with open(report_path, "r") as f:
                    data = json.load(f)
                data["resolved_out_dir"] = str(fs_out_dir)
                return data
            else:
                # Report missing - Hard Fail
                tail = self._tail_log(log_file)
                return {
                    "status": "fail", 
                    "error": "build_report.json missing", 
                    "log_file": log_file,
                    "log_tail": tail
                }

        except Exception as e:
            return {"status": "fail", "error": str(e)}

    def _tail_log(self, log_path: str, lines: int = 80) -> List[str]:
        if not os.path.exists(log_path):
            return ["Log file not found."]
        try:
            with open(log_path, 'r', errors='ignore') as f:
                content = f.readlines()
            return [l.strip() for l in content[-lines:]]
        except:
            return ["Could not read log."]
