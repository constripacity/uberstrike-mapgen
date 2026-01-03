"""Screen Tools"""
import mss, base64
from PIL import Image
from io import BytesIO

async def screenshot(region=None):
    with mss.mss() as sct:
        mon = {"top":region["y"], "left":region["x"], "width":region["w"], "height":region["h"]} if region else sct.monitors[1]
        img = sct.grab(mon)
        pil = Image.frombytes("RGB", img.size, img.bgra, "raw", "BGRX")
        buf = BytesIO()
        pil.save(buf, format="PNG")
        return {"success": True, "width": img.width, "height": img.height, "data": base64.b64encode(buf.getvalue()).decode()}
