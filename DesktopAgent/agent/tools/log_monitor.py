"""
log_monitor.py - Unity Build Log Monitoring Tool for UberAgent Pipeline

Monitors Unity build logs in real-time, detecting build completion, success/failure status,
and extracting error messages. Designed for headless Unity builds in the UberAgent pipeline.
"""

import asyncio
import os
import re
import time
from pathlib import Path
from typing import Dict, List, Optional, Tuple
from datetime import datetime, timedelta
import logging

logger = logging.getLogger(__name__)


class LogMonitorResult:
    """Structured result from log monitoring"""
    
    def __init__(
        self,
        success: bool,
        message: str,
        errors: List[str],
        warnings: List[str] = None,
        completed: bool = False,
        timed_out: bool = False,
        log_excerpt: str = "",
        duration_seconds: float = 0.0
    ):
        self.success = success
        self.message = message
        self.errors = errors or []
        self.warnings = warnings or []
        self.completed = completed
        self.timed_out = timed_out
        self.log_excerpt = log_excerpt
        self.duration_seconds = duration_seconds
    
    def to_dict(self) -> Dict:
        """Convert to dictionary for serialization"""
        return {
            "success": self.success,
            "message": self.message,
            "errors": self.errors,
            "warnings": self.warnings,
            "completed": self.completed,
            "timed_out": self.timed_out,
            "log_excerpt": self.log_excerpt,
            "duration_seconds": self.duration_seconds
        }


class UnityLogMonitor:
    """
    Monitors Unity build logs in real-time for build completion and errors.
    
    Features:
    - Real-time log tailing with async file operations
    - Pattern-based success/failure detection
    - Error and warning extraction
    - Timeout handling
    - Stale log detection (Unity frozen/crashed)
    - Handle locked/missing files gracefully
    """
    
    # Success markers indicating build completion
    SUCCESS_MARKERS = [
        r"\[Headless\]\s+BUILD_DONE",               # will match with or without trailing symbols like ✓
        r"Build completed successfully",
        r"Build succeeded",
        r"\*\*\* Build Finished Successfully \*\*\*"
    ]
    
    # Failure markers indicating build errors
    FAILURE_MARKERS = [
        r"Build failed",
        r"\*\*\* Build Failed \*\*\*",
        r"Compilation failed",
        r"Error building Player",
        r"Build Failed with errors"
    ]
    
    # Error patterns to extract
    ERROR_PATTERNS = [
        r"Error:\s*(.+)",
        r"Exception:\s*(.+)",
        r"CompilerError:\s*(.+)",
        r"Fatal Error:\s*(.+)",
        r"\[ERROR\]\s*(.+)",
        r"Build failed with error:\s*(.+)"
    ]
    
    # Warning patterns to extract
    WARNING_PATTERNS = [
        r"Warning:\s*(.+)",
        r"\[WARNING\]\s*(.+)",
        r"WARN:\s*(.+)"
    ]
    
    def __init__(
        self,
        log_file_path: str,
        timeout_seconds: int = 3600,
        check_interval: float = 0.5,
        stale_threshold: int = 900
    ):
        """
        Initialize log monitor.
        
        Args:
            log_file_path: Path to Unity log file
            timeout_seconds: Maximum time to monitor (default 1 hour)
            check_interval: How often to check for new content (seconds)
            stale_threshold: Consider log stale after this many seconds without updates (default 15 minutes)
        """
        self.log_file_path = Path(log_file_path)
        self.timeout_seconds = timeout_seconds
        self.check_interval = check_interval
        self.stale_threshold = stale_threshold
        
        self.errors: List[str] = []
        self.warnings: List[str] = []
        self.last_position = 0
        self.last_update_time = None
        self.log_lines: List[str] = []
        
        # Compile regex patterns for efficiency
        self.success_patterns = [re.compile(p, re.IGNORECASE) for p in self.SUCCESS_MARKERS]
        self.failure_patterns = [re.compile(p, re.IGNORECASE) for p in self.FAILURE_MARKERS]
        self.error_patterns = [re.compile(p, re.IGNORECASE) for p in self.ERROR_PATTERNS]
        self.warning_patterns = [re.compile(p, re.IGNORECASE) for p in self.WARNING_PATTERNS]
    
    async def wait_for_log_file(self, max_wait: int = 60) -> bool:
        """
        Wait for log file to be created.
        
        Args:
            max_wait: Maximum seconds to wait for file
            
        Returns:
            True if file exists, False if timeout
        """
        start_time = time.time()
        
        while time.time() - start_time < max_wait:
            if self.log_file_path.exists():
                logger.info(f"Log file found: {self.log_file_path}")
                return True
            
            await asyncio.sleep(1)
        
        logger.warning(f"Log file not found after {max_wait}s: {self.log_file_path}")
        return False
    
    async def read_new_lines(self) -> List[str]:
        """
        Read new lines from log file since last read.
        Handles file locks and missing files gracefully.
        
        Returns:
            List of new lines
        """
        if not self.log_file_path.exists():
            return []
        
        try:
            # Try to open file with shared read access
            with open(self.log_file_path, 'r', encoding='utf-8', errors='ignore') as f:
                # Seek to last position
                f.seek(self.last_position)
                
                # Read new content
                new_lines = f.readlines()
                
                # Update position
                self.last_position = f.tell()
                
                if new_lines:
                    self.last_update_time = time.time()
                
                return new_lines
        
        except PermissionError:
            # File is locked by Unity, try again later
            logger.debug("Log file is locked, will retry")
            return []
        
        except Exception as e:
            logger.error(f"Error reading log file: {e}")
            return []
    
    def extract_errors(self, line: str) -> Optional[str]:
        """
        Extract error message from a log line.
        
        Args:
            line: Log line to check
            
        Returns:
            Error message if found, None otherwise
        """
        for pattern in self.error_patterns:
            match = pattern.search(line)
            if match:
                # Return the captured group or the whole match
                return match.group(1) if match.groups() else match.group(0)
        return None
    
    def extract_warnings(self, line: str) -> Optional[str]:
        """
        Extract warning message from a log line.
        
        Args:
            line: Log line to check
            
        Returns:
            Warning message if found, None otherwise
        """
        for pattern in self.warning_patterns:
            match = pattern.search(line)
            if match:
                return match.group(1) if match.groups() else match.group(0)
        return None
    
    def check_success(self, line: str) -> bool:
        """Check if line contains a success marker"""
        return any(pattern.search(line) for pattern in self.success_patterns)
    
    def check_failure(self, line: str) -> bool:
        """Check if line contains a failure marker"""
        return any(pattern.search(line) for pattern in self.failure_patterns)
    
    def is_log_stale(self) -> bool:
        """
        Check if log has become stale (Unity may have frozen/crashed).
        
        Returns:
            True if log hasn't been updated in stale_threshold seconds
        """
        if self.last_update_time is None:
            return False
        
        time_since_update = time.time() - self.last_update_time
        return time_since_update > self.stale_threshold
    
    def get_log_excerpt(self, num_lines: int = 50) -> str:
        """
        Get the last N lines from collected log lines.
        
        Args:
            num_lines: Number of lines to include
            
        Returns:
            Log excerpt as string
        """
        excerpt_lines = self.log_lines[-num_lines:] if self.log_lines else []
        return ''.join(excerpt_lines)
    
    async def monitor_until_complete(self) -> LogMonitorResult:
        """
        Monitor log file until build completes or timeout occurs.
        
        Returns:
            LogMonitorResult with build status and details
        """
        start_time = time.time()
        
        # Wait for log file to be created
        logger.info(f"Waiting for log file: {self.log_file_path}")
        if not await self.wait_for_log_file():
            return LogMonitorResult(
                success=False,
                message="Log file was not created",
                errors=["Log file not found"],
                completed=False
            )
        
        logger.info("Starting log monitoring...")
        self.last_update_time = time.time()
        
        build_success = False
        build_completed = False
        last_progress_log = start_time
        progress_log_interval = 60  # Log progress every 60 seconds
        
        try:
            while True:
                # Check timeout
                elapsed = time.time() - start_time
                
                # Log progress periodically during long builds
                if elapsed - (last_progress_log - start_time) >= progress_log_interval:
                    minutes = int(elapsed / 60)
                    logger.info(f"Still monitoring build... {minutes} minute(s) elapsed")
                    last_progress_log = time.time()
                
                if elapsed > self.timeout_seconds:
                    logger.warning(f"Log monitoring timed out after {elapsed:.1f}s")
                    return LogMonitorResult(
                        success=False,
                        message=f"Build monitoring timed out after {elapsed:.1f} seconds",
                        errors=self.errors,
                        warnings=self.warnings,
                        completed=False,
                        timed_out=True,
                        log_excerpt=self.get_log_excerpt(),
                        duration_seconds=elapsed
                    )
                
                # Check if log is stale
                if self.is_log_stale():
                    logger.warning("Log appears stale - Unity may have frozen or crashed")
                    return LogMonitorResult(
                        success=False,
                        message=f"Log stopped updating (stale for {self.stale_threshold}s) - Unity may have frozen",
                        errors=self.errors + ["Unity process appears to have frozen or crashed"],
                        warnings=self.warnings,
                        completed=False,
                        log_excerpt=self.get_log_excerpt(),
                        duration_seconds=elapsed
                    )
                
                # Read new lines
                new_lines = await self.read_new_lines()
                
                # Process each new line
                for line in new_lines:
                    self.log_lines.append(line)
                    line_stripped = line.strip()
                    
                    # Check for success markers
                    if self.check_success(line_stripped):
                        logger.info(f"Success marker found: {line_stripped}")
                        build_success = True
                        build_completed = True
                    
                    # Check for failure markers
                    if self.check_failure(line_stripped):
                        logger.warning(f"Failure marker found: {line_stripped}")
                        build_success = False
                        build_completed = True
                    
                    # Extract errors
                    error = self.extract_errors(line_stripped)
                    if error:
                        # Classify common licensing/access-token messages as non-fatal warnings
                        error_lower = error.lower()
                        non_fatal_patterns = [
                            'access token is unavailable',
                            'licensingclient has failed validation',
                            'licensingclient has failed validation;',
                            'license',
                            'licensing'
                        ]
                        if any(p in error_lower for p in non_fatal_patterns):
                            if error not in self.warnings:
                                logger.warning(f"Non-fatal licensing warning: {error}")
                                self.warnings.append(error)
                        else:
                            if error not in self.errors:
                                logger.error(f"Error found: {error}")
                                self.errors.append(error)
                    
                    # Extract warnings
                    warning = self.extract_warnings(line_stripped)
                    if warning and warning not in self.warnings:
                        logger.warning(f"Warning found: {warning}")
                        self.warnings.append(warning)
                
                # If build completed, return result
                if build_completed:
                    elapsed = time.time() - start_time
                    message = "Build completed successfully" if build_success else "Build failed"
                    
                    if not build_success and not self.errors:
                        # Failed but no specific errors captured
                        self.errors.append("Build failed - check log for details")
                    
                    logger.info(f"Build monitoring complete: {message} (took {elapsed:.1f}s)")
                    return LogMonitorResult(
                        success=build_success,
                        message=message,
                        errors=self.errors,
                        warnings=self.warnings,
                        completed=True,
                        log_excerpt=self.get_log_excerpt(),
                        duration_seconds=elapsed
                    )
                
                # Wait before next check
                await asyncio.sleep(self.check_interval)
        
        except Exception as e:
            elapsed = time.time() - start_time
            logger.error(f"Error during log monitoring: {e}", exc_info=True)
            return LogMonitorResult(
                success=False,
                message=f"Log monitoring error: {str(e)}",
                errors=self.errors + [str(e)],
                warnings=self.warnings,
                completed=False,
                log_excerpt=self.get_log_excerpt(),
                duration_seconds=elapsed
            )
    
    async def get_last_lines(self, num_lines: int = 100) -> List[str]:
        """
        Get the last N lines from the log file.
        
        Args:
            num_lines: Number of lines to retrieve
            
        Returns:
            List of last N lines
        """
        if not self.log_file_path.exists():
            return []
        
        try:
            with open(self.log_file_path, 'r', encoding='utf-8', errors='ignore') as f:
                lines = f.readlines()
                return lines[-num_lines:] if lines else []
        except Exception as e:
            logger.error(f"Error reading last lines: {e}")
            return []
    
    async def check_current_status(self) -> Dict:
        """
        Check current status of log file without continuous monitoring.
        
        Returns:
            Dictionary with current status information
        """
        if not self.log_file_path.exists():
            return {
                "exists": False,
                "message": "Log file not found"
            }
        
        try:
            # Get file stats
            stats = self.log_file_path.stat()
            file_size = stats.st_size
            modified_time = datetime.fromtimestamp(stats.st_mtime)
            
            # Read last lines
            last_lines = await self.get_last_lines(50)
            
            # Check for completion markers
            has_success = any(
                any(pattern.search(line) for pattern in self.success_patterns)
                for line in last_lines
            )
            has_failure = any(
                any(pattern.search(line) for pattern in self.failure_patterns)
                for line in last_lines
            )
            
            # Extract recent errors
            recent_errors = []
            for line in last_lines:
                error = self.extract_errors(line.strip())
                if error:
                    recent_errors.append(error)
            
            return {
                "exists": True,
                "file_size": file_size,
                "modified_time": modified_time.isoformat(),
                "has_success_marker": has_success,
                "has_failure_marker": has_failure,
                "recent_errors": recent_errors,
                "last_lines": [line.strip() for line in last_lines[-10:]]
            }
        
        except Exception as e:
            logger.error(f"Error checking log status: {e}")
            return {
                "exists": True,
                "error": str(e)
            }


# Tool functions for Desktop Agent integration

async def monitor_unity_build_log(
    log_file_path: str = "C:/UberStrikeGen/Logs/headless_pipeline.log",
    timeout_seconds: int = 3600,
    check_interval: float = 0.5,
    stale_threshold: int = 900
) -> Dict:
    """
    Monitor Unity build log until completion or timeout.
    
    Args:
        log_file_path: Path to Unity log file
        timeout_seconds: Maximum time to monitor (default 1 hour)
        check_interval: How often to check for new content (seconds)
        stale_threshold: Consider log stale after this many seconds without updates (default 15 minutes)
    
    Returns:
        Dictionary with build result information
    """
    monitor = UnityLogMonitor(
        log_file_path=log_file_path,
        timeout_seconds=timeout_seconds,
        check_interval=check_interval,
        stale_threshold=stale_threshold
    )
    
    result = await monitor.monitor_until_complete()
    return result.to_dict()


async def get_log_tail(
    log_file_path: str = "C:/UberStrikeGen/Logs/headless_pipeline.log",
    num_lines: int = 100
) -> Dict:
    """
    Get the last N lines from a log file.
    
    Args:
        log_file_path: Path to log file
        num_lines: Number of lines to retrieve
    
    Returns:
        Dictionary with log lines and metadata
    """
    monitor = UnityLogMonitor(log_file_path)
    lines = await monitor.get_last_lines(num_lines)
    
    return {
        "log_file": str(log_file_path),
        "num_lines_requested": num_lines,
        "num_lines_returned": len(lines),
        "lines": [line.strip() for line in lines]
    }


async def check_log_status(
    log_file_path: str = "C:/UberStrikeGen/Logs/headless_pipeline.log"
) -> Dict:
    """
    Check current status of Unity log file without continuous monitoring.
    
    Args:
        log_file_path: Path to log file
    
    Returns:
        Dictionary with current log status
    """
    monitor = UnityLogMonitor(log_file_path)
    status = await monitor.check_current_status()
    status["log_file"] = str(log_file_path)
    return status


# Tool registration metadata for Desktop Agent
TOOL_METADATA = {
    "monitor_unity_build_log": {
        "name": "monitor_unity_build_log",
        "description": "Monitor Unity build log in real-time until build completes or timeout. Detects success/failure and extracts errors.",
        "parameters": {
            "log_file_path": {
                "type": "string",
                "description": "Path to Unity log file",
                "default": "C:/UberStrikeGen/Logs/headless_pipeline.log"
            },
            "timeout_seconds": {
                "type": "integer",
                "description": "Maximum time to monitor in seconds",
                "default": 3600
            },
            "check_interval": {
                "type": "number",
                "description": "How often to check for new content (seconds)",
                "default": 0.5
            },
            "stale_threshold": {
                "type": "integer",
                "description": "Consider log stale after this many seconds without updates",
                "default": 900
            }
        },
        "returns": {
            "type": "object",
            "properties": {
                "success": {"type": "boolean"},
                "message": {"type": "string"},
                "errors": {"type": "array"},
                "warnings": {"type": "array"},
                "completed": {"type": "boolean"},
                "timed_out": {"type": "boolean"},
                "log_excerpt": {"type": "string"},
                "duration_seconds": {"type": "number"}
            }
        }
    },
    "get_log_tail": {
        "name": "get_log_tail",
        "description": "Get the last N lines from a log file",
        "parameters": {
            "log_file_path": {
                "type": "string",
                "description": "Path to log file",
                "default": "C:/UberStrikeGen/Logs/headless_pipeline.log"
            },
            "num_lines": {
                "type": "integer",
                "description": "Number of lines to retrieve",
                "default": 100
            }
        },
        "returns": {
            "type": "object",
            "properties": {
                "log_file": {"type": "string"},
                "num_lines_requested": {"type": "integer"},
                "num_lines_returned": {"type": "integer"},
                "lines": {"type": "array"}
            }
        }
    },
    "check_log_status": {
        "name": "check_log_status",
        "description": "Check current status of Unity log file without continuous monitoring",
        "parameters": {
            "log_file_path": {
                "type": "string",
                "description": "Path to log file",
                "default": "C:/UberStrikeGen/Logs/headless_pipeline.log"
            }
        },
        "returns": {
            "type": "object",
            "properties": {
                "exists": {"type": "boolean"},
                "file_size": {"type": "integer"},
                "modified_time": {"type": "string"},
                "has_success_marker": {"type": "boolean"},
                "has_failure_marker": {"type": "boolean"},
                "recent_errors": {"type": "array"},
                "last_lines": {"type": "array"}
            }
        }
    }
}

__all__ = [
    "UnityLogMonitor",
    "LogMonitorResult",
    "monitor_unity_build_log",
    "get_log_tail",
    "check_log_status",
    "TOOL_METADATA",
]
