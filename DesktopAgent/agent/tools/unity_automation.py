"""
Unity Automation Tool for UberAgent Pipeline
Automates Unity headless builds for FPS map generation from ML blueprints.
"""

import asyncio
import os
import re
import signal
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path
from typing import Optional, Dict, Any, List, Tuple

# --- UPDATED IMPORTS (relative imports) ---
from .log_monitor import monitor_unity_build_log, check_log_status

# Tool metadata for Desktop Agent registry
TOOL_METADATA = {
    "name": "unity_automation",
    "description": "Automates Unity headless builds for UberAgent FPS map generation pipeline",
    "version": "1.2.0",  # Fixed timeout issues for long builds
    "author": "UberAgent Team",
    "capabilities": [
        "launch_unity_headless",
        "execute_blueprint_build",
        "generate_map",
        "monitor_unity_process",
        "parse_unity_logs",
        "detect_build_completion"
    ]
}

class UnityAutomationError(Exception):
    """Base exception for Unity automation errors."""
    pass

class UnityProcessError(UnityAutomationError):
    """Raised when Unity process fails or crashes."""
    pass

class UnityTimeoutError(UnityAutomationError):
    """Raised when Unity operations timeout."""
    pass

class UnityBuildError(UnityAutomationError):
    """Raised when Unity build fails."""
    pass

class UnityAutomation:
    """
    Manages Unity automation for the UberAgent pipeline.
    Handles headless builds, process monitoring, and log parsing.
    """

    # Default paths (can be overridden)
    UNITY_EDITOR_PATH = "C:/Program Files/Unity/Hub/Editor/6000.2.6f2/Editor/Unity.exe"
    PROJECT_PATH = "C:/UberStrikeGen"
    DEFAULT_BLUEPRINT_DIR = "C:/UberStrikeGen/Assets/_UberStrike/Blueprints/MapLayouts"
    DEFAULT_OUTPUT_DIR = "C:/UberStrikeGen/Assets/_UberStrike/Maps/Playable"
    DEFAULT_LOG_FILE = "C:/UberStrikeGen/Logs/headless_pipeline.log"

    # Unity build methods
    BLUEPRINT_BUILD_METHOD = "BuildFromBlueprint.BuildFromPNGPath"
    MAP_GEN_METHOD = "HeadlessBuilder.GenerateMap"

    # Timeouts (seconds)
    STARTUP_TIMEOUT = 120
    BUILD_TIMEOUT = 3600
    SHUTDOWN_TIMEOUT = 30

    # Log markers
    LOG_MARKER_READY = "Refreshing native plugins compatible for Editor"

    def __init__(
        self,
        unity_path: Optional[str] = None,
        project_path: Optional[str] = None,
        log_file: Optional[str] = None,
        logger=None
    ):
        """
        Initialize Unity automation.
        """
        self.unity_path = unity_path or self.UNITY_EDITOR_PATH
        self.project_path = project_path or self.PROJECT_PATH
        self.log_file = log_file or self.DEFAULT_LOG_FILE
        self.logger = logger

        self.process: Optional[subprocess.Popen] = None
        self.is_running = False

        # Validate paths
        self._validate_paths()

    def _find_unity_executable(self) -> str:
        """Find Unity executable path"""
        return self.UNITY_EDITOR_PATH

    def _validate_paths(self):
        """Validate required paths exist."""
        if not Path(self.unity_path).exists():
            self._log(f"WARNING: Unity executable not found at: {self.unity_path}", level="warning")

        if not Path(self.project_path).exists():
            self._log(f"WARNING: Unity project not found at: {self.project_path}", level="warning")

        log_dir = Path(self.log_file).parent
        log_dir.mkdir(parents=True, exist_ok=True)

    def _log(self, message: str, level: str = "info"):
        """Log a message."""
        timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        formatted_msg = f"[{timestamp}] [UnityAutomation] [{level.upper()}] {message}"

        if self.logger:
            log_method = getattr(self.logger, level, self.logger.info)
            log_method(message)
        else:
            print(formatted_msg)

    def _build_unity_command(self, custom_args: Optional[List[str]] = None) -> List[str]:
        """Build the base Unity command-line arguments.

        Place custom_args (for example -executeMethod/--args) before the -quit flag
        so Unity receives the executeMethod call prior to shutdown.
        """
        cmd = [
            self.unity_path,
            "-batchmode",
            "-nographics",
            "-projectPath", self.project_path,
        ]
        if custom_args:
            cmd.extend(custom_args)
        cmd.extend(["-logFile", self.log_file, "-quit"])
        return cmd

    async def launch_and_monitor(self, command_args: List[str], timeout: int) -> Dict[str, Any]:
        """A generic function to launch Unity and monitor the log."""
        if self.is_running:
            return {"success": False, "error": "Unity is already running"}

        try:
            cmd = self._build_unity_command(command_args)
            self._log(f"Launching Unity: {' '.join(cmd)}")

            if Path(self.log_file).exists():
                os.remove(self.log_file)

            self.process = subprocess.Popen(
                cmd,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                creationflags=subprocess.CREATE_NO_WINDOW if sys.platform == "win32" else 0
            )
            self.is_running = True
            self._log(f"Unity process started (PID: {self.process.pid})")

            monitor_result = await monitor_unity_build_log(
                log_file_path=self.log_file,
                timeout_seconds=timeout,
                check_interval=0.5,
                stale_threshold=900
            )

            return self._process_monitor_result(monitor_result)

        except Exception as e:
            self._log(f"Failed to launch or monitor Unity: {e}", level="error")
            return {"success": False, "error": str(e)}
        finally:
            await self.shutdown(force=True)

    def _process_monitor_result(self, monitor_result: Dict[str, Any]) -> Dict[str, Any]:
        """Processes the result from the log monitor into a final return dictionary."""
        if monitor_result.get('success') and monitor_result.get('completed'):
            return {
                "success": True,
                "message": "Unity task completed successfully.",
                "log_file": self.log_file,
                "errors": monitor_result.get('errors', []),
                "warnings": monitor_result.get('warnings', []),
                "build_time": f"{monitor_result.get('duration_seconds', 0):.1f}s",
                "output_scenes": self._find_output_scenes()
            }
        else:
            return {
                "success": False,
                "message": monitor_result.get('message', 'Unknown error'),
                "errors": monitor_result.get('errors', []),
                "warnings": monitor_result.get('warnings', []),
                "timed_out": monitor_result.get('timed_out', False),
                "log_excerpt": monitor_result.get('log_excerpt', '')
            }

    async def execute_blueprint_build(self, blueprint_path: str, timeout: Optional[int] = None, mpp: Optional[float] = None) -> Dict[str, Any]:
        """Execute a headless build from a blueprint.

        Uses the parameterless headless wrapper `UnityCI.Headless.BuildArena` which
        parses additional key=value pairs passed after the `--args` flag.
        """
        if not Path(blueprint_path).exists():
            return {"success": False, "error": f"Blueprint not found: {blueprint_path}"}

        self._log(f"Starting build from blueprint: {blueprint_path}")

        mpp_value = float(mpp) if mpp is not None else 0.20

        build_args = [
            "-executeMethod", "UnityCI.Headless.BuildArena",
            "--args",
            f"blueprint={str(Path(blueprint_path))}",
            f"mpp={mpp_value}"
        ]

        return await self.launch_and_monitor(build_args, timeout or self.BUILD_TIMEOUT)

    async def generate_map(self, map_name: str, style: str = "industrial", size: str = "medium", timeout: Optional[int] = None) -> Dict[str, Any]:
        """Generate a new map using Unity's headless pipeline.

        Prefer using the headless wrapper that accepts --args (UnityCI.Headless.BuildArena).
        If a matching blueprint PNG exists in the blueprint folder, call execute_blueprint_build
        which correctly passes --args blueprint=... mpp=... to the parameterless wrapper.
        For legacy fallback the retry logic will ONLY attempt UnityCI.Headless.BuildArena to avoid
        calling broken executeMethod variants that crash Unity (e.g. BuildFromBlueprint.BuildFromPNG).
        """
        valid_styles = ["industrial", "urban", "facility", "outdoor"]
        valid_sizes = ["small", "medium", "large"]
        if style not in valid_styles:
            return {"success": False, "error": f"Invalid style. Must be one of: {valid_styles}"}
        if size not in valid_sizes:
            return {"success": False, "error": f"Invalid size. Must be one of: {valid_sizes}"}

        self._log(f"Starting map generation: {map_name} (Style: {style}, Size: {size})")

        # If a blueprint PNG exists with this map_name (case-insensitive), use the blueprint build path.
        blueprint_dir = Path(self.DEFAULT_BLUEPRINT_DIR)
        candidate_blueprint = blueprint_dir / f"{map_name}.png"
        found_blueprint = None

        if candidate_blueprint.exists():
            found_blueprint = candidate_blueprint
        else:
            if blueprint_dir.exists():
                target_lower = f"{map_name}.png".lower()
                for f in blueprint_dir.glob("*.png"):
                    if f.name.lower() == target_lower:
                        found_blueprint = f
                        break

        if found_blueprint:
            self._log(f"Found blueprint for '{map_name}' at: {found_blueprint}")
            return await self.execute_blueprint_build(str(found_blueprint), timeout or self.BUILD_TIMEOUT, mpp=0.20)

        # Before launching Unity, ensure there are no conflicting Unity.exe processes for the same project.
        if sys.platform == "win32":
            try:
                # Use a safe, well-formed WMIC command string
                wmic_cmd = "wmic process where \"name='Unity.exe'\" get ProcessId,CommandLine"
                wmic = subprocess.run(wmic_cmd, capture_output=True, text=True, shell=True)
                out = (wmic.stdout or "") + (wmic.stderr or "")

                if self.project_path and self.project_path in out:
                    # Kill any Unity.exe process that appears to have been launched for the same project
                    for line in out.splitlines():
                        if self.project_path in line:
                            m = re.search(r'(\d+)\s*$', line.strip())
                            if m:
                                pid = m.group(1)
                                self._log(f"Found Unity.exe running for same project (PID: {pid}). Attempting to kill.", level="warning")
                                subprocess.run(['taskkill', '/PID', pid, '/F'], check=False)
                                await asyncio.sleep(0.5)
                                break
                else:
                    # WMIC didn't reveal a matching command line. If any Unity.exe exists, refuse to start
                    tasklist = subprocess.run('tasklist | findstr Unity.exe', capture_output=True, text=True, shell=True)
                    if tasklist.stdout.strip():
                        return {"success": False, "error": "Unity.exe is already running. Please close existing instances before starting a new build."}
            except Exception as e:
                self._log(f"Error checking Unity processes: {e}", level="warning")

        # Only attempt the known-working headless wrapper that accepts --args key=val pairs.
        candidate_methods = ["UnityCI.Headless.BuildArena"]

        attempts = 0
        max_attempts = 3
        last_error: Optional[Dict[str, Any]] = None

        while attempts < max_attempts:
            method_to_try = candidate_methods[0]

            gen_args = [
                "-executeMethod", method_to_try,
                "--args",
                f"mapName={map_name}",
                f"mapStyle={style}",
                f"mapSize={size}"
            ]

            self._log(f"Attempt {attempts+1}/{max_attempts}: using executeMethod '{method_to_try}'")

            result = await self.launch_and_monitor(gen_args, timeout or self.BUILD_TIMEOUT)

            if result.get("success") and result.get("timed_out") is not True:
                return result

            message = (result.get("message") or "").lower()
            log_excerpt = (result.get("log_excerpt") or "").lower()

            # Retry for transient failures/timeouts but do not try legacy broken methods.
            if "executeMethod class" in log_excerpt or "could not be found" in log_excerpt or "argument was -executemethod" in log_excerpt or "executemethod" in message or "has 2 arguments" in log_excerpt:
                self._log(f"executeMethod failure detected for '{method_to_try}'. Retrying headless wrapper.", level="warning")
                last_error = result
                attempts += 1
                await asyncio.sleep(1)
                continue

            if result.get("timed_out") or not result.get("success"):
                self._log(f"Unity run failed or timed out (attempt {attempts+1}): {result.get('message')}", level="warning")
                last_error = result
                attempts += 1
                await asyncio.sleep(2)
                continue

            return result

        self._log("All generate_map attempts failed. Returning detailed error.", level="error")
        attempted_methods = candidate_methods[:max_attempts]
        detail = {
            "success": False,
            "error": "generate_map failed after multiple attempts",
            "attempted_methods": attempted_methods,
            "last_result": last_error or {}
        }
        return detail

    def _find_output_scenes(self) -> List[str]:
        """Find recently generated scene files in the output directory."""
        try:
            output_dir = Path(self.DEFAULT_OUTPUT_DIR)
            if not output_dir.exists():
                return []

            recent_time = time.time() - 300
            return [str(f) for f in output_dir.glob("*.unity") if f.stat().st_mtime > recent_time]
        except Exception as e:
            self._log(f"Error finding output scenes: {e}", level="warning")
            return []

    async def shutdown(self, force: bool = False) -> Dict[str, Any]:
        """Shutdown the Unity process."""
        if not self.process or self.process.poll() is not None:
            self.is_running = False
            return {"success": True, "message": "No running process to shutdown."}

        self._log(f"Shutting down Unity (force={force})...")
        try:
            if force:
                self.process.kill()
            else:
                self.process.terminate()
                self.process.wait(timeout=self.SHUTDOWN_TIMEOUT)

            self._log("Unity shutdown complete.")
            return {"success": True, "message": "Unity shutdown complete"}
        except subprocess.TimeoutExpired:
            self._log("Graceful shutdown timeout, force killing.", level="warning")
            self.process.kill()
            return {"success": True, "message": "Unity forcefully shut down after timeout."}
        except Exception as e:
            self._log(f"Error during shutdown: {e}", level="error")
            return {"success": False, "error": str(e)}
        finally:
            self.is_running = False

    def get_status(self) -> Dict[str, Any]:
        """Get current Unity automation status."""
        is_alive = self.process is not None and self.process.poll() is None
        return {
            "is_running": is_alive,
            "pid": self.process.pid if is_alive else None,
            "unity_path": self.unity_path,
            "project_path": self.project_path,
        }

# -----------------------------------------------------------------------------
# --- Blueprint Discovery Function ---
# -----------------------------------------------------------------------------
async def find_blueprints(
    blueprint_dir: str = UnityAutomation.DEFAULT_BLUEPRINT_DIR,
    pattern: str = "*.png"
) -> Dict:
    """
    Find available blueprint files for map generation.

    Args:
        blueprint_dir: Directory to search for blueprints
        pattern: File pattern to match (default: *.png')

    Returns:
        Dictionary with list of found blueprints
    """
    blueprint_path = Path(blueprint_dir)

    if not blueprint_path.exists():
        return {
            "success": False,
            "error": f"Blueprint directory not found: {blueprint_dir}",
            "blueprints": []
        }

    try:
        blueprints = []
        for file in blueprint_path.glob(pattern):
            blueprints.append({
                "name": file.stem,
                "path": str(file),
                "size": file.stat().st_size,
                "modified": datetime.fromtimestamp(file.stat().st_mtime).isoformat()
            })

        blueprints.sort(key=lambda x: x["name"])

        return {
            "success": True,
            "blueprint_dir": str(blueprint_path),
            "count": len(blueprints),
            "blueprints": blueprints
        }

    except Exception as e:
        return {
            "success": False,
            "error": f"Error searching for blueprints: {str(e)}",
            "blueprints": []
        }

# ============================================================================
# Desktop Agent Tool Interface (Example)
# ============================================================================
async def execute_tool(action: str, params: Dict[str, Any]) -> Dict[str, Any]:
    """Main entry point for Desktop Agent tool execution."""
    unity = UnityAutomation(logger=params.get("logger"))

    try:
        if action == "execute_blueprint_build":
            return await unity.execute_blueprint_build(
                blueprint_path=params["blueprint_path"],
                timeout=params.get("timeout")
            )
        elif action == "generate_map":
            return await unity.generate_map(
                map_name=params["map_name"],
                style=params.get("style", "industrial"),
                size=params.get("size", "medium"),
                timeout=params.get("timeout")
            )
        elif action == "find_blueprints":
            return await find_blueprints(
                blueprint_dir=params.get("blueprint_dir", UnityAutomation.DEFAULT_BLUEPRINT_DIR),
                pattern=params.get("pattern", "*.png")
            )
        elif action == "get_status":
            return {"success": True, "status": unity.get_status()}
        elif action == "shutdown":
            return await unity.shutdown(force=params.get("force", False))
        else:
            return {"success": False, "error": f"Unknown action: {action}"}
    except Exception as e:
        return {
            "success": False,
            "error": str(e),
            "traceback": __import__('traceback').format_exc()
        }

# ============================================================================
# CLI Interface (for testing)
# ============================================================================
async def main():
    """CLI interface for testing Unity automation."""
    import argparse, json

    parser = argparse.ArgumentParser(description="Unity Automation Tool")
    parser.add_argument("action", choices=[
        "build", "generate", "status", "shutdown", "find_blueprints"
    ])
    parser.add_argument("--blueprint", help="Path to blueprint PNG for 'build' action")
    parser.add_argument("--map-name", help="Name for the new map for 'generate' action")
    parser.add_argument("--style", default="industrial", help="Map style for 'generate' action")
    parser.add_argument("--size", default="medium", help="Map size for 'generate' action")
    parser.add_argument("--force", action="store_true", help="Force shutdown")
    parser.add_argument("--timeout", type=int, help="Build timeout")

    # Defaults aligned with class constants and PNG blueprints
    parser.add_argument("--blueprint-dir", default=UnityAutomation.DEFAULT_BLUEPRINT_DIR,
                        help="Directory to search for blueprints (for 'find_blueprints')")
    parser.add_argument("--pattern", default="*.png",
                        help="Filename pattern for blueprints (for 'find_blueprints')")

    args = parser.parse_args()

    unity = UnityAutomation()
    result = None

    try:
        if args.action == "build":
            if not args.blueprint:
                print("Error: --blueprint is required for the 'build' action.")
                return
            result = await unity.execute_blueprint_build(args.blueprint, args.timeout)

        elif args.action == "generate":
            if not args.map_name:
                print("Error: --map-name is required for the 'generate' action.")
                return
            result = await unity.generate_map(args.map_name, args.style, args.size, args.timeout)

        elif args.action == "status":
            result = unity.get_status()

        elif args.action == "shutdown":
            result = await unity.shutdown(force=args.force)

        elif args.action == "find_blueprints":
            result = await find_blueprints(
                blueprint_dir=args.blueprint_dir,
                pattern=args.pattern
            )

        if result:
            print(json.dumps(result, indent=2))

    except Exception as e:
        print(f"An error occurred: {e}")
        print(__import__('traceback').format_exc())

if __name__ == "__main__":
    asyncio.run(main())
