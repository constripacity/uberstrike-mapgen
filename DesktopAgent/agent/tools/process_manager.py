"""
Process Manager Tool for Desktop Agent
Manages process lifecycle, monitoring, and resource usage on Windows.
"""

import asyncio
import psutil
import subprocess
import logging
from typing import Optional, Dict, List, Any, Tuple
from pathlib import Path
from datetime import datetime
import time

logger = logging.getLogger(__name__)


class ProcessManager:
    """Manages process operations including launch, monitor, and resource tracking."""
    
    def __init__(self):
        self.monitored_processes: Dict[int, Dict[str, Any]] = {}
        self.process_history: List[Dict[str, Any]] = []
        
    async def launch_process(
        self,
        executable_path: str,
        arguments: Optional[List[str]] = None,
        working_directory: Optional[str] = None,
        wait_for_startup: bool = True,
        startup_timeout: int = 30,
        environment: Optional[Dict[str, str]] = None
    ) -> Dict[str, Any]:
        """
        Launch an executable with arguments.
        
        Args:
            executable_path: Full path to executable
            arguments: List of command-line arguments
            working_directory: Working directory for the process
            wait_for_startup: Wait for process to fully start
            startup_timeout: Timeout in seconds for startup
            environment: Environment variables to set
            
        Returns:
            Dict with process info including PID, status, and timing
        """
        try:
            exe_path = Path(executable_path)
            if not exe_path.exists():
                return {
                    "success": False,
                    "error": f"Executable not found: {executable_path}",
                    "path": executable_path
                }
            
            if not exe_path.is_file():
                return {
                    "success": False,
                    "error": f"Path is not a file: {executable_path}",
                    "path": executable_path
                }
            
            # Build command
            cmd = [str(exe_path)]
            if arguments:
                cmd.extend(arguments)
            
            # Set working directory
            cwd = working_directory if working_directory else exe_path.parent
            
            logger.info(f"Launching process: {' '.join(cmd)}")
            logger.info(f"Working directory: {cwd}")
            
            start_time = time.time()
            
            # Launch process
            creation_flags = subprocess.CREATE_NEW_PROCESS_GROUP
            if environment:
                import os
                env = os.environ.copy()
                env.update(environment)
            else:
                env = None
            
            process = await asyncio.create_subprocess_exec(
                *cmd,
                cwd=str(cwd),
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                creationflags=creation_flags,
                env=env
            )
            
            pid = process.pid
            
            # Wait for process to actually start
            if wait_for_startup:
                await asyncio.sleep(1)  # Give it a moment to initialize
                
                timeout_time = time.time() + startup_timeout
                while time.time() < timeout_time:
                    if psutil.pid_exists(pid):
                        try:
                            ps_process = psutil.Process(pid)
                            if ps_process.status() in [psutil.STATUS_RUNNING, psutil.STATUS_SLEEPING]:
                                break
                        except (psutil.NoSuchProcess, psutil.AccessDenied):
                            pass
                    await asyncio.sleep(0.5)
                else:
                    return {
                        "success": False,
                        "error": "Process startup timeout",
                        "pid": pid,
                        "timeout": startup_timeout
                    }
            
            startup_time = time.time() - start_time
            
            # Get process details
            try:
                ps_process = psutil.Process(pid)
                process_info = {
                    "success": True,
                    "pid": pid,
                    "name": ps_process.name(),
                    "status": ps_process.status(),
                    "executable": executable_path,
                    "arguments": arguments or [],
                    "working_directory": str(cwd),
                    "startup_time": startup_time,
                    "created_time": datetime.fromtimestamp(ps_process.create_time()).isoformat()
                }
                
                # Add to monitored processes
                self.monitored_processes[pid] = {
                    "process": ps_process,
                    "launch_info": process_info,
                    "subprocess": process
                }
                
                logger.info(f"Process launched successfully: PID {pid}, startup time {startup_time:.2f}s")
                return process_info
                
            except (psutil.NoSuchProcess, psutil.AccessDenied) as e:
                return {
                    "success": False,
                    "error": f"Failed to get process info: {str(e)}",
                    "pid": pid
                }
                
        except Exception as e:
            logger.error(f"Failed to launch process: {str(e)}")
            return {
                "success": False,
                "error": str(e),
                "executable": executable_path
            }
    
    async def is_process_running(
        self,
        process_name: Optional[str] = None,
        pid: Optional[int] = None
    ) -> Dict[str, Any]:
        """
        Check if a process is running by name or PID.
        
        Args:
            process_name: Name of the process (e.g., "Unity.exe")
            pid: Process ID
            
        Returns:
            Dict with running status and process details
        """
        try:
            if pid:
                if psutil.pid_exists(pid):
                    try:
                        process = psutil.Process(pid)
                        return {
                            "running": True,
                            "pid": pid,
                            "name": process.name(),
                            "status": process.status(),
                            "exe": process.exe()
                        }
                    except (psutil.NoSuchProcess, psutil.AccessDenied):
                        return {"running": False, "pid": pid}
                else:
                    return {"running": False, "pid": pid}
            
            elif process_name:
                # Search for process by name
                matching_processes = []
                for proc in psutil.process_iter(['pid', 'name', 'status', 'exe']):
                    try:
                        if proc.info['name'].lower() == process_name.lower():
                            matching_processes.append({
                                "pid": proc.info['pid'],
                                "name": proc.info['name'],
                                "status": proc.info['status'],
                                "exe": proc.info['exe']
                            })
                    except (psutil.NoSuchProcess, psutil.AccessDenied):
                        continue
                
                if matching_processes:
                    return {
                        "running": True,
                        "process_name": process_name,
                        "count": len(matching_processes),
                        "processes": matching_processes
                    }
                else:
                    return {
                        "running": False,
                        "process_name": process_name
                    }
            
            else:
                return {
                    "success": False,
                    "error": "Must provide either process_name or pid"
                }
                
        except Exception as e:
            logger.error(f"Error checking process status: {str(e)}")
            return {
                "success": False,
                "error": str(e)
            }
    
    async def monitor_process_status(
        self,
        pid: int,
        detailed: bool = True
    ) -> Dict[str, Any]:
        """
        Monitor detailed process status including health checks.
        
        Args:
            pid: Process ID to monitor
            detailed: Include detailed resource usage
            
        Returns:
            Dict with comprehensive process status
        """
        try:
            if not psutil.pid_exists(pid):
                return {
                    "success": True,
                    "pid": pid,
                    "status": "not_found",
                    "running": False
                }
            
            process = psutil.Process(pid)
            
            # Basic status
            status_info = {
                "success": True,
                "pid": pid,
                "name": process.name(),
                "status": process.status(),
                "running": True,
                "created_time": datetime.fromtimestamp(process.create_time()).isoformat()
            }
            
            # Check for problematic states
            proc_status = process.status()
            if proc_status == psutil.STATUS_ZOMBIE:
                status_info["health"] = "zombie"
                status_info["healthy"] = False
            elif proc_status == psutil.STATUS_DEAD:
                status_info["health"] = "dead"
                status_info["healthy"] = False
                status_info["running"] = False
            elif proc_status == psutil.STATUS_STOPPED:
                status_info["health"] = "stopped"
                status_info["healthy"] = False
            else:
                # Check if responding (Windows-specific)
                if detailed:
                    try:
                        # Check CPU usage - if 0 for extended period, might be hung
                        cpu_percent = process.cpu_percent(interval=0.1)
                        memory_info = process.memory_info()
                        
                        status_info["responding"] = True
                        status_info["health"] = "healthy"
                        status_info["healthy"] = True
                        status_info["cpu_percent"] = cpu_percent
                        status_info["memory_mb"] = memory_info.rss / (1024 * 1024)
                        
                    except (psutil.NoSuchProcess, psutil.AccessDenied) as e:
                        status_info["health"] = "unknown"
                        status_info["healthy"] = False
                        status_info["error"] = str(e)
                else:
                    status_info["health"] = "running"
                    status_info["healthy"] = True
            
            # Get command line
            try:
                status_info["cmdline"] = process.cmdline()
            except (psutil.NoSuchProcess, psutil.AccessDenied):
                status_info["cmdline"] = []
            
            # Get executable path
            try:
                status_info["exe"] = process.exe()
            except (psutil.NoSuchProcess, psutil.AccessDenied):
                status_info["exe"] = None
            
            # Get parent process
            try:
                parent = process.parent()
                if parent:
                    status_info["parent_pid"] = parent.pid
                    status_info["parent_name"] = parent.name()
            except (psutil.NoSuchProcess, psutil.AccessDenied):
                pass
            
            return status_info
            
        except psutil.NoSuchProcess:
            return {
                "success": True,
                "pid": pid,
                "status": "terminated",
                "running": False,
                "healthy": False
            }
        except Exception as e:
            logger.error(f"Error monitoring process {pid}: {str(e)}")
            return {
                "success": False,
                "pid": pid,
                "error": str(e)
            }
    
    async def kill_process(
        self,
        process_name: Optional[str] = None,
        pid: Optional[int] = None,
        force: bool = False,
        timeout: int = 10
    ) -> Dict[str, Any]:
        """
        Kill a process by name or PID.
        
        Args:
            process_name: Name of process to kill
            pid: Process ID to kill
            force: Use forceful termination (SIGKILL)
            timeout: Timeout in seconds to wait for graceful shutdown
            
        Returns:
            Dict with kill operation results
        """
        try:
            killed_processes = []
            failed_processes = []
            
            processes_to_kill = []
            
            # Find processes to kill
            if pid:
                if psutil.pid_exists(pid):
                    try:
                        processes_to_kill.append(psutil.Process(pid))
                    except (psutil.NoSuchProcess, psutil.AccessDenied) as e:
                        return {
                            "success": False,
                            "error": f"Cannot access process {pid}: {str(e)}"
                        }
                else:
                    return {
                        "success": False,
                        "error": f"Process {pid} not found"
                    }
            
            elif process_name:
                for proc in psutil.process_iter(['pid', 'name']):
                    try:
                        if proc.info['name'].lower() == process_name.lower():
                            processes_to_kill.append(psutil.Process(proc.info['pid']))
                    except (psutil.NoSuchProcess, psutil.AccessDenied):
                        continue
                
                if not processes_to_kill:
                    return {
                        "success": False,
                        "error": f"No processes found with name: {process_name}"
                    }
            else:
                return {
                    "success": False,
                    "error": "Must provide either process_name or pid"
                }
            
            # Kill each process
            for process in processes_to_kill:
                try:
                    proc_pid = process.pid
                    proc_name = process.name()
                    
                    logger.info(f"Killing process: {proc_name} (PID: {proc_pid})")
                    
                    if force:
                        # Force kill
                        process.kill()
                        killed_processes.append({
                            "pid": proc_pid,
                            "name": proc_name,
                            "method": "force"
                        })
                    else:
                        # Graceful termination
                        process.terminate()
                        
                        # Wait for process to terminate
                        try:
                            process.wait(timeout=timeout)
                            killed_processes.append({
                                "pid": proc_pid,
                                "name": proc_name,
                                "method": "graceful"
                            })
                        except psutil.TimeoutExpired:
                            # Force kill if graceful fails
                            logger.warning(f"Graceful termination timeout for PID {proc_pid}, forcing...")
                            process.kill()
                            killed_processes.append({
                                "pid": proc_pid,
                                "name": proc_name,
                                "method": "force_after_timeout"
                            })
                    
                    # Remove from monitored processes
                    if proc_pid in self.monitored_processes:
                        del self.monitored_processes[proc_pid]
                    
                except (psutil.NoSuchProcess, psutil.AccessDenied) as e:
                    failed_processes.append({
                        "pid": process.pid,
                        "name": process.name(),
                        "error": str(e)
                    })
                    logger.error(f"Failed to kill process {process.pid}: {str(e)}")
            
            return {
                "success": True,
                "killed_count": len(killed_processes),
                "failed_count": len(failed_processes),
                "killed_processes": killed_processes,
                "failed_processes": failed_processes
            }
            
        except Exception as e:
            logger.error(f"Error killing process: {str(e)}")
            return {
                "success": False,
                "error": str(e)
            }
    
    async def get_resource_usage(
        self,
        pid: int,
        interval: float = 1.0
    ) -> Dict[str, Any]:
        """
        Get detailed resource usage for a process.
        
        Args:
            pid: Process ID
            interval: Interval for CPU measurement in seconds
            
        Returns:
            Dict with CPU, memory, disk, and network usage
        """
        try:
            if not psutil.pid_exists(pid):
                return {
                    "success": False,
                    "error": f"Process {pid} not found"
                }
            
            process = psutil.Process(pid)
            
            # CPU usage
            cpu_percent = process.cpu_percent(interval=interval)
            cpu_times = process.cpu_times()
            
            # Memory usage
            memory_info = process.memory_info()
            memory_percent = process.memory_percent()
            
            # I/O counters (if available)
            try:
                io_counters = process.io_counters()
                io_info = {
                    "read_count": io_counters.read_count,
                    "write_count": io_counters.write_count,
                    "read_bytes": io_counters.read_bytes,
                    "write_bytes": io_counters.write_bytes,
                    "read_mb": io_counters.read_bytes / (1024 * 1024),
                    "write_mb": io_counters.write_bytes / (1024 * 1024)
                }
            except (AttributeError, psutil.AccessDenied):
                io_info = None
            
            # Thread count
            num_threads = process.num_threads()
            
            # File handles
            try:
                num_handles = process.num_handles()
            except (AttributeError, psutil.AccessDenied):
                num_handles = None
            
            # Connection count (network)
            try:
                connections = process.connections()
                num_connections = len(connections)
            except (psutil.AccessDenied, psutil.NoSuchProcess):
                num_connections = None
            
            resource_info = {
                "success": True,
                "pid": pid,
                "name": process.name(),
                "timestamp": datetime.now().isoformat(),
                "cpu": {
                    "percent": cpu_percent,
                    "user_time": cpu_times.user,
                    "system_time": cpu_times.system
                },
                "memory": {
                    "rss_bytes": memory_info.rss,
                    "rss_mb": memory_info.rss / (1024 * 1024),
                    "vms_bytes": memory_info.vms,
                    "vms_mb": memory_info.vms / (1024 * 1024),
                    "percent": memory_percent
                },
                "threads": num_threads,
                "handles": num_handles,
                "connections": num_connections
            }
            
            if io_info:
                resource_info["io"] = io_info
            
            return resource_info
            
        except psutil.NoSuchProcess:
            return {
                "success": False,
                "error": f"Process {pid} no longer exists"
            }
        except Exception as e:
            logger.error(f"Error getting resource usage for PID {pid}: {str(e)}")
            return {
                "success": False,
                "error": str(e)
            }
    
    async def get_all_processes(
        self,
        filter_name: Optional[str] = None
    ) -> Dict[str, Any]:
        """
        Get list of all running processes, optionally filtered by name.
        
        Args:
            filter_name: Filter by process name (case-insensitive partial match)
            
        Returns:
            Dict with list of processes
        """
        try:
            processes = []
            
            for proc in psutil.process_iter(['pid', 'name', 'status', 'cpu_percent', 'memory_percent']):
                try:
                    proc_info = proc.info
                    
                    if filter_name:
                        if filter_name.lower() not in proc_info['name'].lower():
                            continue
                    
                    processes.append({
                        "pid": proc_info['pid'],
                        "name": proc_info['name'],
                        "status": proc_info['status'],
                        "cpu_percent": proc_info['cpu_percent'] or 0.0,
                        "memory_percent": proc_info['memory_percent'] or 0.0
                    })
                    
                except (psutil.NoSuchProcess, psutil.AccessDenied):
                    continue
            
            # Sort by CPU usage (descending)
            processes.sort(key=lambda x: x['cpu_percent'], reverse=True)
            
            return {
                "success": True,
                "count": len(processes),
                "processes": processes,
                "filter": filter_name
            }
            
        except Exception as e:
            logger.error(f"Error getting process list: {str(e)}")
            return {
                "success": False,
                "error": str(e)
            }
    
    async def wait_for_process_exit(
        self,
        pid: int,
        timeout: Optional[int] = None
    ) -> Dict[str, Any]:
        """
        Wait for a process to exit.
        
        Args:
            pid: Process ID to wait for
            timeout: Timeout in seconds (None for no timeout)
            
        Returns:
            Dict with exit status and timing
        """
        try:
            if not psutil.pid_exists(pid):
                return {
                    "success": True,
                    "pid": pid,
                    "exited": True,
                    "wait_time": 0,
                    "exit_code": None
                }
            
            process = psutil.Process(pid)
            start_time = time.time()
            
            # Get subprocess if monitored
            subprocess_obj = None
            if pid in self.monitored_processes:
                subprocess_obj = self.monitored_processes[pid].get('subprocess')
            
            try:
                # Use psutil wait if no timeout
                if timeout is None:
                    return_code = process.wait()
                    wait_time = time.time() - start_time
                    
                    return {
                        "success": True,
                        "pid": pid,
                        "exited": True,
                        "exit_code": return_code,
                        "wait_time": wait_time
                    }
                else:
                    # Poll with timeout
                    poll_interval = 0.5
                    elapsed = 0
                    
                    while elapsed < timeout:
                        if not psutil.pid_exists(pid):
                            wait_time = time.time() - start_time
                            
                            exit_code = None
                            if subprocess_obj:
                                exit_code = subprocess_obj.returncode
                            
                            return {
                                "success": True,
                                "pid": pid,
                                "exited": True,
                                "exit_code": exit_code,
                                "wait_time": wait_time
                            }
                        
                        await asyncio.sleep(poll_interval)
                        elapsed += poll_interval
                    
                    # Timeout reached
                    return {
                        "success": True,
                        "pid": pid,
                        "exited": False,
                        "timeout": True,
                        "wait_time": timeout
                    }
                    
            except psutil.TimeoutExpired:
                return {
                    "success": True,
                    "pid": pid,
                    "exited": False,
                    "timeout": True,
                    "wait_time": timeout
                }
            
        except psutil.NoSuchProcess:
            return {
                "success": True,
                "pid": pid,
                "exited": True,
                "wait_time": 0,
                "exit_code": None
            }
        except Exception as e:
            logger.error(f"Error waiting for process {pid}: {str(e)}")
            return {
                "success": False,
                "error": str(e)
            }


# Global instance
_process_manager = ProcessManager()


# Tool registry functions
async def launch_process(
    executable_path: str,
    arguments: Optional[List[str]] = None,
    working_directory: Optional[str] = None,
    wait_for_startup: bool = True,
    startup_timeout: int = 30,
    environment: Optional[Dict[str, str]] = None
) -> Dict[str, Any]:
    """Launch an executable with arguments."""
    return await _process_manager.launch_process(
        executable_path=executable_path,
        arguments=arguments,
        working_directory=working_directory,
        wait_for_startup=wait_for_startup,
        startup_timeout=startup_timeout,
        environment=environment
    )


async def is_process_running(
    process_name: Optional[str] = None,
    pid: Optional[int] = None
) -> Dict[str, Any]:
    """Check if a process is running by name or PID."""
    return await _process_manager.is_process_running(
        process_name=process_name,
        pid=pid
    )


async def monitor_process_status(
    pid: int,
    detailed: bool = True
) -> Dict[str, Any]:
    """Monitor detailed process status including health checks."""
    return await _process_manager.monitor_process_status(
        pid=pid,
        detailed=detailed
    )


async def kill_process(
    process_name: Optional[str] = None,
    pid: Optional[int] = None,
    force: bool = False,
    timeout: int = 10
) -> Dict[str, Any]:
    """Kill a process by name or PID."""
    return await _process_manager.kill_process(
        process_name=process_name,
        pid=pid,
        force=force,
        timeout=timeout
    )


async def get_resource_usage(
    pid: int,
    interval: float = 1.0
) -> Dict[str, Any]:
    """Get detailed resource usage for a process."""
    return await _process_manager.get_resource_usage(
        pid=pid,
        interval=interval
    )


async def get_all_processes(
    filter_name: Optional[str] = None
) -> Dict[str, Any]:
    """Get list of all running processes."""
    return await _process_manager.get_all_processes(
        filter_name=filter_name
    )


async def wait_for_process_exit(
    pid: int,
    timeout: Optional[int] = None
) -> Dict[str, Any]:
    """Wait for a process to exit."""
    return await _process_manager.wait_for_process_exit(
        pid=pid,
        timeout=timeout
    )


# Tool metadata for registry
TOOL_METADATA = {
    "name": "process_manager",
    "description": "Manage Windows processes: launch, monitor, kill, and track resource usage",
    "functions": {
        "launch_process": {
            "description": "Launch an executable with arguments and monitor startup",
            "parameters": {
                "executable_path": "Full path to executable file",
                "arguments": "List of command-line arguments (optional)",
                "working_directory": "Working directory for process (optional)",
                "wait_for_startup": "Wait for process to fully start (default: True)",
                "startup_timeout": "Startup timeout in seconds (default: 30)",
                "environment": "Environment variables dict (optional)"
            }
        },
        "is_process_running": {
            "description": "Check if a process is running by name or PID",
            "parameters": {
                "process_name": "Name of process (e.g., 'Unity.exe') (optional)",
                "pid": "Process ID (optional)"
            }
        },
        "monitor_process_status": {
            "description": "Get detailed process status including health checks",
            "parameters": {
                "pid": "Process ID to monitor",
                "detailed": "Include detailed resource usage (default: True)"
            }
        },
        "kill_process": {
            "description": "Terminate a process by name or PID",
            "parameters": {
                "process_name": "Name of process to kill (optional)",
                "pid": "Process ID to kill (optional)",
                "force": "Force kill if graceful fails (default: False)",
                "timeout": "Timeout for graceful shutdown (default: 10)"
            }
        },
        "get_resource_usage": {
            "description": "Get CPU, memory, disk, and network usage for a process",
            "parameters": {
                "pid": "Process ID",
                "interval": "CPU measurement interval in seconds (default: 1.0)"
            }
        },
        "get_all_processes": {
            "description": "List all running processes with optional name filter",
            "parameters": {
                "filter_name": "Filter by process name (optional)"
            }
        },
        "wait_for_process_exit": {
            "description": "Wait for a process to exit with optional timeout",
            "parameters": {
                "pid": "Process ID to wait for",
                "timeout": "Timeout in seconds, None for no timeout (optional)"
            }
        }
    }
}
