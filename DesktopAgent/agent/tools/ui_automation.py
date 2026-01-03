"""UI Automation"""
import time
from pynput.mouse import Button, Controller as Mouse
from pynput.keyboard import Controller as Keyboard

mouse, kbd = Mouse(), Keyboard()

async def click_at(x, y, button="left"):
    try:
        btn = {"left": Button.left, "right": Button.right}.get(button, Button.left)
        mouse.position = (x, y)
        time.sleep(0.05)
        mouse.click(btn)
        return {"success": True, "x": x, "y": y}
    except Exception as e:
        return {"success": False, "error": str(e)}

async def type_text(text, interval=0.05):
    try:
        for c in text:
            kbd.press(c)
            kbd.release(c)
            time.sleep(interval)
        return {"success": True, "length": len(text)}
    except Exception as e:
        return {"success": False, "error": str(e)}
