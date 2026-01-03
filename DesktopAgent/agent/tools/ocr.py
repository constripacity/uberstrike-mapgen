# tools/ocr.py
from typing import Optional, Dict, Tuple, List
import pytesseract, mss
from PIL import Image

def _grab(region):
    with mss.mss() as sct:
        if region:
            bbox={"left":region["x"],"top":region["y"],"width":region["w"],"height":region["h"]}
            img = sct.grab(bbox)
        else:
            img = sct.grab(sct.monitors[1])
    return Image.frombytes("RGB",(img.width,img.height),img.rgb)

def _ocr(region):
    im=_grab(region)
    data = pytesseract.image_to_data(im, output_type=pytesseract.Output.DICT)
    boxes=[]
    for i in range(len(data["text"])):
        t=data["text"][i]
        if not t.strip(): continue
        x,y,w,h = data["left"][i], data["top"][i], data["width"][i], data["height"][i]
        boxes.append((t,(x,y,w,h)))
    return "\n".join([b[0] for b in boxes]), boxes

async def ocr_region(region: Optional[Dict[str,int]]=None, **kw) -> str:
    """Run OCR over region and return plain text."""
    txt,_ = _ocr(region)
    return txt
