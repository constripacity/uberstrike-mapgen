"""B0 Blender smoke test driver (TCP fallback path).

Talks directly to the Blender MCP addon over localhost:9876 using the same
line-delimited JSON protocol the MCP server uses. Lets us run the smoke
test from a Claude Code session that hadn't loaded the MCP server yet.

Smoke test:
1. Reads Tools/blender_smoke/smoke_16x16.png
2. Tells Blender to clear the default scene and drop one 0.9-unit cube per
   pixel, colored from the PNG, on a 16x16 grid.
3. Frames the result with an ortho top-down camera.
4. Pulls a viewport screenshot back to Tools/blender_smoke/smoke_result.png.
"""

import json
import os
import socket
from PIL import Image

HOST = "127.0.0.1"
PORT = 9876
HERE = os.path.dirname(os.path.abspath(__file__))
PNG_IN = os.path.join(HERE, "smoke_16x16.png")
PNG_OUT = os.path.join(HERE, "smoke_result.png")


def send(cmd_type: str, params: dict, timeout: float = 60.0) -> dict:
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.settimeout(timeout)
    s.connect((HOST, PORT))
    try:
        s.sendall((json.dumps({"type": cmd_type, "params": params}) + "\n").encode())
        chunks = []
        while True:
            data = s.recv(65536)
            if not data:
                break
            chunks.append(data)
            try:
                json.loads(b"".join(chunks).decode())
                break
            except json.JSONDecodeError:
                continue
        return json.loads(b"".join(chunks).decode())
    finally:
        s.close()


def load_pixels(path: str):
    img = Image.open(path).convert("RGB")
    w, h = img.size
    return w, h, [[img.getpixel((x, y)) for x in range(w)] for y in range(h)]


def main():
    print(f"[smoke] reading {PNG_IN}")
    w, h, pixels = load_pixels(PNG_IN)
    print(f"[smoke] image {w}x{h}")

    print("[smoke] probing Blender (get_scene_info)")
    info = send("get_scene_info", {})
    print(f"  -> {info.get('status')}: object_count={info.get('result', {}).get('object_count')}")

    code = f"""
import bpy

# Clean the scene.
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for m in list(bpy.data.materials):
    bpy.data.materials.remove(m)
for me in list(bpy.data.meshes):
    bpy.data.meshes.remove(me)

W, H = {w}, {h}
PIXELS = {json.dumps(pixels)}

# Place one 0.9-unit cube per pixel, colored by the PNG.
mat_cache = {{}}
for y in range(H):
    for x in range(W):
        r, g, b = PIXELS[y][x]
        bpy.ops.mesh.primitive_cube_add(size=0.9, location=(x - W/2 + 0.5, (H - 1 - y) - H/2 + 0.5, 0))
        cube = bpy.context.active_object
        cube.name = f'cube_{{x}}_{{y}}'
        key = (r, g, b)
        if key not in mat_cache:
            m = bpy.data.materials.new(name=f'pix_{{r}}_{{g}}_{{b}}')
            m.use_nodes = True
            bsdf = m.node_tree.nodes.get('Principled BSDF')
            if bsdf is not None:
                bsdf.inputs['Base Color'].default_value = (r/255.0, g/255.0, b/255.0, 1.0)
            mat_cache[key] = m
        cube.data.materials.append(mat_cache[key])

# Top-down ortho camera framing the grid.
cam_data = bpy.data.cameras.new('SmokeCam')
cam_data.type = 'ORTHO'
cam_data.ortho_scale = max(W, H) + 2
cam = bpy.data.objects.new('SmokeCam', cam_data)
bpy.context.scene.collection.objects.link(cam)
cam.location = (0, 0, max(W, H))
cam.rotation_euler = (0, 0, 0)
bpy.context.scene.camera = cam

# Sun light so the colors actually read.
sun_data = bpy.data.lights.new('SmokeSun', type='SUN')
sun_data.energy = 3.0
sun = bpy.data.objects.new('SmokeSun', sun_data)
bpy.context.scene.collection.objects.link(sun)
sun.location = (0, 0, max(W, H) + 5)

# Frame the camera in viewport.
for area in bpy.context.screen.areas:
    if area.type == 'VIEW_3D':
        for space in area.spaces:
            if space.type == 'VIEW_3D':
                space.region_3d.view_perspective = 'CAMERA'

print(f'[smoke] placed {{W*H}} cubes, {{len(mat_cache)}} unique materials')
""".strip()

    print(f"[smoke] sending execute_code ({len(code)} chars)")
    res = send("execute_code", {"code": code}, timeout=120.0)
    print(f"  -> {res.get('status')}: {res.get('result', res.get('message'))}")
    if res.get("status") != "success":
        return 1

    print("[smoke] requesting viewport screenshot")
    shot = send("get_viewport_screenshot", {"max_size": 1024, "filepath": PNG_OUT, "format": "png"}, timeout=30.0)
    print(f"  -> {shot.get('status')}: {shot.get('result', shot.get('message'))}")

    if os.path.exists(PNG_OUT):
        print(f"[smoke] screenshot saved: {PNG_OUT} ({os.path.getsize(PNG_OUT)} bytes)")
        return 0
    print("[smoke] WARN: screenshot file not present at expected path")
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
