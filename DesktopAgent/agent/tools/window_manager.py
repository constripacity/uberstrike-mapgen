"""
Window Manager Tool for Desktop Agent
Manages window operations: finding, focusing, positioning, and listing windows
"""

import asyncio
import logging
from typing import Optional, Dict, List, Any
import win32gui
import win32con
import win32process
import win32api
from dataclasses import dataclass
from datetime import datetime

logger = logging.getLogger(__name__)


@dataclass
class WindowInfo:
    """Data class to store window information"""
    hwnd: int
    title: str
    class_name: str
    rect: tuple  # (left, top, right, bottom)
    is_visible: bool
    is_enabled: bool
    process_id: int
    thread_id: int


class WindowManager:
    """Manages window operations using Win32 API"""
    
    def __init__(self):
        self.timeout_seconds = 5
        
    async def find_window_by_title(self, partial_title: str, case_sensitive: bool = False) -> Optional[WindowInfo]:
        """
        Find a window by partial title match
        
        Args:
            partial_title: Partial window title to search for
            case_sensitive: Whether to perform case-sensitive search
            
        Returns:
            WindowInfo object if found, None otherwise
        """
        try:
            logger.info(f"Searching for window with title containing: '{partial_title}'")
            
            search_title = partial_title if case_sensitive else partial_title.lower()
            found_window = None
            
            def enum_callback(hwnd, _):
                nonlocal found_window
                if win32gui.IsWindowVisible(hwnd):
                    window_title = win32gui.GetWindowText(hwnd)
                    compare_title = window_title if case_sensitive else window_title.lower()
                    
                    if search_title in compare_title:
                        found_window = self._get_window_info(hwnd)
                        return False  # Stop enumeration
                return True  # Continue enumeration
            
            # Run in executor to avoid blocking
            await asyncio.get_event_loop().run_in_executor(
                None, 
                win32gui.EnumWindows, 
                enum_callback, 
                None
            )
            
            if found_window:
                logger.info(f"Found window: '{found_window.title}' (HWND: {found_window.hwnd})")
            else:
                logger.warning(f"No window found with title containing: '{partial_title}'")
                
            return found_window
            
        except Exception as e:
            logger.error(f"Error finding window: {e}", exc_info=True)
            return None
    
    async def bring_window_to_foreground(self, hwnd: int) -> bool:
        """
        Bring a window to the foreground
        
        Args:
            hwnd: Window handle
            
        Returns:
            True if successful, False otherwise
        """
        try:
            logger.info(f"Bringing window {hwnd} to foreground")
            
            def _bring_to_front():
                # Check if window exists and is valid
                if not win32gui.IsWindow(hwnd):
                    raise ValueError(f"Invalid window handle: {hwnd}")
                
                # If window is minimized, restore it
                if win32gui.IsIconic(hwnd):
                    win32gui.ShowWindow(hwnd, win32con.SW_RESTORE)
                
                # Bring window to foreground
                win32gui.SetForegroundWindow(hwnd)
                
                # Alternative method if SetForegroundWindow fails
                try:
                    win32gui.BringWindowToTop(hwnd)
                    win32gui.SetFocus(hwnd)
                except:
                    pass
                
                return True
            
            result = await asyncio.get_event_loop().run_in_executor(None, _bring_to_front)
            
            if result:
                logger.info(f"Successfully brought window {hwnd} to foreground")
            
            return result
            
        except Exception as e:
            logger.error(f"Error bringing window to foreground: {e}", exc_info=True)
            return False
    
    async def get_window_rect(self, hwnd: int) -> Optional[Dict[str, int]]:
        """
        Get window coordinates and dimensions
        
        Args:
            hwnd: Window handle
            
        Returns:
            Dictionary with x, y, width, height, or None if failed
        """
        try:
            def _get_rect():
                if not win32gui.IsWindow(hwnd):
                    raise ValueError(f"Invalid window handle: {hwnd}")
                
                left, top, right, bottom = win32gui.GetWindowRect(hwnd)
                return {
                    'x': left,
                    'y': top,
                    'width': right - left,
                    'height': bottom - top,
                    'left': left,
                    'top': top,
                    'right': right,
                    'bottom': bottom
                }
            
            rect = await asyncio.get_event_loop().run_in_executor(None, _get_rect)
            logger.info(f"Window {hwnd} rect: {rect}")
            return rect
            
        except Exception as e:
            logger.error(f"Error getting window rect: {e}", exc_info=True)
            return None
    
    async def is_window_responsive(self, hwnd: int, timeout: int = 5000) -> bool:
        """
        Check if a window is responsive (not frozen)
        
        Args:
            hwnd: Window handle
            timeout: Timeout in milliseconds (default 5000ms)
            
        Returns:
            True if responsive, False if frozen or error
        """
        try:
            def _check_responsive():
                if not win32gui.IsWindow(hwnd):
                    return False
                
                # Try to send a message to the window
                try:
                    result = win32gui.SendMessageTimeout(
                        hwnd,
                        win32con.WM_NULL,
                        0,
                        0,
                        win32con.SMTO_ABORTIFHUNG,
                        timeout
                    )
                    # result is a tuple (return_value, result_value)
                    # If it returns without exception, window is responsive
                    return True
                except Exception:
                    return False
            
            responsive = await asyncio.get_event_loop().run_in_executor(None, _check_responsive)
            
            if responsive:
                logger.info(f"Window {hwnd} is responsive")
            else:
                logger.warning(f"Window {hwnd} is not responsive or frozen")
            
            return responsive
            
        except Exception as e:
            logger.error(f"Error checking window responsiveness: {e}", exc_info=True)
            return False
    
    async def list_all_windows(self, visible_only: bool = True) -> List[Dict[str, Any]]:
        """
        List all open windows with their information
        
        Args:
            visible_only: If True, only return visible windows
            
        Returns:
            List of dictionaries containing window information
        """
        try:
            logger.info("Listing all windows")
            windows = []
            
            def enum_callback(hwnd, _):
                try:
                    if visible_only and not win32gui.IsWindowVisible(hwnd):
                        return True
                    
                    window_title = win32gui.GetWindowText(hwnd)
                    
                    # Skip windows without titles
                    if not window_title:
                        return True
                    
                    window_info = self._get_window_info(hwnd)
                    windows.append({
                        'hwnd': window_info.hwnd,
                        'title': window_info.title,
                        'class_name': window_info.class_name,
                        'rect': {
                            'left': window_info.rect[0],
                            'top': window_info.rect[1],
                            'right': window_info.rect[2],
                            'bottom': window_info.rect[3],
                            'width': window_info.rect[2] - window_info.rect[0],
                            'height': window_info.rect[3] - window_info.rect[1]
                        },
                        'is_visible': window_info.is_visible,
                        'is_enabled': window_info.is_enabled,
                        'process_id': window_info.process_id,
                        'thread_id': window_info.thread_id
                    })
                except Exception as e:
                    logger.debug(f"Error processing window {hwnd}: {e}")
                
                return True
            
            await asyncio.get_event_loop().run_in_executor(
                None,
                win32gui.EnumWindows,
                enum_callback,
                None
            )
            
            logger.info(f"Found {len(windows)} windows")
            return windows
            
        except Exception as e:
            logger.error(f"Error listing windows: {e}", exc_info=True)
            return []
    
    def _get_window_info(self, hwnd: int) -> WindowInfo:
        """
        Get detailed information about a window
        
        Args:
            hwnd: Window handle
            
        Returns:
            WindowInfo object
        """
        try:
            title = win32gui.GetWindowText(hwnd)
            class_name = win32gui.GetClassName(hwnd)
            rect = win32gui.GetWindowRect(hwnd)
            is_visible = win32gui.IsWindowVisible(hwnd)
            is_enabled = win32gui.IsWindowEnabled(hwnd)
            thread_id, process_id = win32process.GetWindowThreadProcessId(hwnd)
            
            return WindowInfo(
                hwnd=hwnd,
                title=title,
                class_name=class_name,
                rect=rect,
                is_visible=is_visible,
                is_enabled=is_enabled,
                process_id=process_id,
                thread_id=thread_id
            )
        except Exception as e:
            logger.error(f"Error getting window info for {hwnd}: {e}")
            raise


# Global instance
_window_manager = WindowManager()


# Tool functions for Desktop Agent registry
async def find_window(partial_title: str, case_sensitive: bool = False) -> Dict[str, Any]:
    """
    Find a window by partial title match
    
    Args:
        partial_title: Partial window title to search for (e.g., "Unity", "Chrome", "Notepad")
        case_sensitive: Whether search should be case-sensitive (default: False)
    
    Returns:
        Dictionary with window information or error message
    """
    try:
        window_info = await _window_manager.find_window_by_title(partial_title, case_sensitive)
        
        if window_info:
            return {
                'success': True,
                'window': {
                    'hwnd': window_info.hwnd,
                    'title': window_info.title,
                    'class_name': window_info.class_name,
                    'x': window_info.rect[0],
                    'y': window_info.rect[1],
                    'width': window_info.rect[2] - window_info.rect[0],
                    'height': window_info.rect[3] - window_info.rect[1],
                    'is_visible': window_info.is_visible,
                    'is_enabled': window_info.is_enabled,
                    'process_id': window_info.process_id
                }
            }
        else:
            return {
                'success': False,
                'error': f"No window found with title containing '{partial_title}'"
            }
    except Exception as e:
        logger.error(f"Error in find_window: {e}", exc_info=True)
        return {
            'success': False,
            'error': str(e)
        }


async def focus_window(hwnd: int) -> Dict[str, Any]:
    """
    Bring a window to the foreground and focus it
    
    Args:
        hwnd: Window handle (integer)
    
    Returns:
        Dictionary indicating success or failure
    """
    try:
        success = await _window_manager.bring_window_to_foreground(hwnd)
        
        if success:
            return {
                'success': True,
                'message': f'Window {hwnd} brought to foreground'
            }
        else:
            return {
                'success': False,
                'error': f'Failed to bring window {hwnd} to foreground'
            }
    except Exception as e:
        logger.error(f"Error in focus_window: {e}", exc_info=True)
        return {
            'success': False,
            'error': str(e)
        }


async def get_window_geometry(hwnd: int) -> Dict[str, Any]:
    """
    Get window position and dimensions
    
    Args:
        hwnd: Window handle (integer)
    
    Returns:
        Dictionary with window geometry (x, y, width, height) or error
    """
    try:
        rect = await _window_manager.get_window_rect(hwnd)
        
        if rect:
            return {
                'success': True,
                'geometry': rect
            }
        else:
            return {
                'success': False,
                'error': f'Failed to get geometry for window {hwnd}'
            }
    except Exception as e:
        logger.error(f"Error in get_window_geometry: {e}", exc_info=True)
        return {
            'success': False,
            'error': str(e)
        }


async def check_window_responsive(hwnd: int, timeout_ms: int = 5000) -> Dict[str, Any]:
    """
    Check if a window is responsive (not frozen)
    
    Args:
        hwnd: Window handle (integer)
        timeout_ms: Timeout in milliseconds (default: 5000)
    
    Returns:
        Dictionary indicating if window is responsive
    """
    try:
        is_responsive = await _window_manager.is_window_responsive(hwnd, timeout_ms)
        
        return {
            'success': True,
            'hwnd': hwnd,
            'is_responsive': is_responsive,
            'message': 'Window is responsive' if is_responsive else 'Window is not responding'
        }
    except Exception as e:
        logger.error(f"Error in check_window_responsive: {e}", exc_info=True)
        return {
            'success': False,
            'error': str(e)
        }


async def list_windows(visible_only: bool = True) -> Dict[str, Any]:
    """
    List all open windows
    
    Args:
        visible_only: Only include visible windows (default: True)
    
    Returns:
        Dictionary with list of all windows and their properties
    """
    try:
        windows = await _window_manager.list_all_windows(visible_only)
        
        return {
            'success': True,
            'count': len(windows),
            'windows': windows
        }
    except Exception as e:
        logger.error(f"Error in list_windows: {e}", exc_info=True)
        return {
            'success': False,
            'error': str(e)
        }


async def find_and_focus_window(partial_title: str, case_sensitive: bool = False) -> Dict[str, Any]:
    """
    Find a window by title and bring it to foreground (convenience function)
    
    Args:
        partial_title: Partial window title to search for
        case_sensitive: Whether search should be case-sensitive
    
    Returns:
        Dictionary with result of find and focus operation
    """
    try:
        # Find the window
        find_result = await find_window(partial_title, case_sensitive)
        
        if not find_result['success']:
            return find_result
        
        # Focus the window
        hwnd = find_result['window']['hwnd']
        focus_result = await focus_window(hwnd)
        
        if focus_result['success']:
            return {
                'success': True,
                'window': find_result['window'],
                'message': f"Found and focused window: {find_result['window']['title']}"
            }
        else:
            return {
                'success': False,
                'window': find_result['window'],
                'error': f"Found window but failed to focus: {focus_result.get('error', 'Unknown error')}"
            }
    except Exception as e:
        logger.error(f"Error in find_and_focus_window: {e}", exc_info=True)
        return {
            'success': False,
            'error': str(e)
        }


# Tool registry configuration
TOOLS = [
    {
        'name': 'find_window',
        'function': find_window,
        'description': 'Find a window by partial title match',
        'parameters': {
            'partial_title': {
                'type': 'string',
                'description': 'Partial window title to search for (e.g., "Unity", "Chrome")',
                'required': True
            },
            'case_sensitive': {
                'type': 'boolean',
                'description': 'Whether search should be case-sensitive',
                'required': False,
                'default': False
            }
        }
    },
    {
        'name': 'focus_window',
        'function': focus_window,
        'description': 'Bring a specific window to foreground',
        'parameters': {
            'hwnd': {
                'type': 'integer',
                'description': 'Window handle obtained from find_window',
                'required': True
            }
        }
    },
    {
        'name': 'get_window_geometry',
        'function': get_window_geometry,
        'description': 'Get window coordinates and dimensions',
        'parameters': {
            'hwnd': {
                'type': 'integer',
                'description': 'Window handle',
                'required': True
            }
        }
    },
    {
        'name': 'check_window_responsive',
        'function': check_window_responsive,
        'description': 'Verify if window is responsive (not frozen)',
        'parameters': {
            'hwnd': {
                'type': 'integer',
                'description': 'Window handle',
                'required': True
            },
            'timeout_ms': {
                'type': 'integer',
                'description': 'Timeout in milliseconds',
                'required': False,
                'default': 5000
            }
        }
    },
    {
        'name': 'list_windows',
        'function': list_windows,
        'description': 'List all open windows with titles and information',
        'parameters': {
            'visible_only': {
                'type': 'boolean',
                'description': 'Only include visible windows',
                'required': False,
                'default': True
            }
        }
    },
    {
        'name': 'find_and_focus_window',
        'function': find_and_focus_window,
        'description': 'Find a window by title and bring it to foreground (convenience function)',
        'parameters': {
            'partial_title': {
                'type': 'string',
                'description': 'Partial window title to search for',
                'required': True
            },
            'case_sensitive': {
                'type': 'boolean',
                'description': 'Whether search should be case-sensitive',
                'required': False,
                'default': False
            }
        }
    }
]


# For testing
if __name__ == '__main__':
    async def test():
        print("Testing Window Manager...")
        
        # List all windows
        print("\n1. Listing all windows:")
        result = await list_windows()
        print(f"Found {result['count']} windows")
        for win in result['windows'][:5]:  # Show first 5
            print(f"  - {win['title']} (HWND: {win['hwnd']})")
        
        # Find a window (example: Explorer or any common window)
        print("\n2. Finding window with 'Explorer' in title:")
        result = await find_window('Explorer')
        if result['success']:
            print(f"  Found: {result['window']['title']}")
            hwnd = result['window']['hwnd']
            
            # Get geometry
            print("\n3. Getting window geometry:")
            geom = await get_window_geometry(hwnd)
            if geom['success']:
                print(f"  Geometry: {geom['geometry']}")
            
            # Check responsiveness
            print("\n4. Checking if window is responsive:")
            resp = await check_window_responsive(hwnd)
            print(f"  Responsive: {resp['is_responsive']}")
        else:
            print(f"  {result['error']}")
    
    # Run test
    asyncio.run(test())