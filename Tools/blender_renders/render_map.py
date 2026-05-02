"""B3 Blender render driver: minimap + 3/4 thumbnail for a generated MapGen map.

Talks directly to the Blender MCP addon over localhost:9876 (same line-delimited
JSON protocol the MCP server uses), so it works whether or not Claude Code's
MCP wrapper is loaded.

Pipeline:
1. Reads a generated map dir (the one OneClickGenerateWindow writes), parses
   the .stack.json for the map name + .layout.png path.
2. Sends a single execute_code blob to Blender that:
   - Clears the active scene
   - Reads the layout PNG and rebuilds the same geometry BuildFromBlueprint
     produces: 64x64m floor plane, 4m wall cubes for black pixels (merged into
     one mesh), spawn cylinders for yellow pixels.
   - Adds a sun + ambient.
   - Adds two cameras: ortho top-down (minimap) and 3/4 perspective (thumbnail).
   - Cycles-renders both at the requested resolutions back into the map dir.
3. Verifies the two PNGs exist on disk.

Usage:
    python Tools/blender_renders/render_map.py \\
        --map-dir Assets/_UberStrike/Generated/OneClick_64_Mixed_1337_20260502_231230 \\
        [--minimap-size 512] [--thumb-w 1024] [--thumb-h 768] [--samples 64]

Prerequisites: Blender open, BlenderMCP addon enabled, "Start MCP Server" pressed
(default port 9876).
"""

import argparse
import json
import os
import socket
import sys

HOST = "127.0.0.1"
PORT = 9876
WALL_HEIGHT = 4.0
SPAWN_HEIGHT = 3.0
SPAWN_RADIUS = 0.5


def send(cmd_type: str, params: dict, timeout: float = 600.0) -> dict:
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


def find_layout_png(map_dir: str) -> tuple[str, str]:
    """Return (map_name, layout_png_abs_path) by inspecting the directory."""
    map_dir = os.path.abspath(map_dir)
    if not os.path.isdir(map_dir):
        raise FileNotFoundError(f"Map directory not found: {map_dir}")

    layout = None
    for fn in os.listdir(map_dir):
        if fn.endswith(".layout.png"):
            layout = os.path.join(map_dir, fn)
            break
    if not layout:
        raise FileNotFoundError(f"No *.layout.png in {map_dir}")
    map_name = os.path.basename(layout)[: -len(".layout.png")]
    return map_name, layout


def build_blender_script(layout_png: str, out_dir: str, map_name: str,
                         minimap_size: int, thumb_w: int, thumb_h: int,
                         samples: int) -> str:
    """Build the inline Blender script. Substitutes paths + ints; no untrusted input is allowed."""
    return f"""
import bpy, bmesh, os
from math import radians

LAYOUT_PNG = r"{layout_png}"
OUT_DIR = r"{out_dir}"
MAP_NAME = "{map_name}"
WALL_HEIGHT = {WALL_HEIGHT}
SPAWN_HEIGHT = {SPAWN_HEIGHT}
SPAWN_RADIUS = {SPAWN_RADIUS}

# --- Clean scene ---
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for blk in list(bpy.data.meshes): bpy.data.meshes.remove(blk)
for blk in list(bpy.data.materials): bpy.data.materials.remove(blk)
for blk in list(bpy.data.images):
    if blk.filepath and ".layout.png" in blk.filepath:
        bpy.data.images.remove(blk)

# --- Read layout PNG ---
img = bpy.data.images.load(LAYOUT_PNG)
W, H = img.size[0], img.size[1]
C = img.channels
px = list(img.pixels)

def get_pixel(x, y):
    idx = ((H - 1 - y) * W + x) * C
    r = int(px[idx] * 255 + 0.5)
    g = int(px[idx+1] * 255 + 0.5)
    b = int(px[idx+2] * 255 + 0.5)
    return r, g, b

def is_wall(rgb):
    r, g, b = rgb
    return r < 30 and g < 30 and b < 30

def is_spawn(rgb):
    r, g, b = rgb
    return r > 200 and g > 200 and b < 80

def make_mat(name, base, roughness=0.7, emission=None):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*base, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = 2.0
    return mat

mat_floor = make_mat("MapGen_Floor", (0.45, 0.45, 0.50), 0.6)
mat_wall  = make_mat("MapGen_Wall",  (0.18, 0.18, 0.22), 0.85)
mat_spawn = make_mat("MapGen_Spawn", (1.0, 0.9, 0.2), 0.3, emission=(1.0, 0.9, 0.2))

# --- Floor plane ---
bpy.ops.mesh.primitive_plane_add(size=W, location=(0, 0, 0))
floor = bpy.context.active_object
floor.name = "Floor"
floor.data.materials.append(mat_floor)

# --- Walls (single merged mesh) ---
wall_mesh = bpy.data.meshes.new("WallsMesh")
walls_obj = bpy.data.objects.new("Walls", wall_mesh)
bpy.context.collection.objects.link(walls_obj)
walls_obj.data.materials.append(mat_wall)
bm = bmesh.new()
spawn_positions = []

for y in range(H):
    for x in range(W):
        rgb = get_pixel(x, y)
        wx = x - W * 0.5 + 0.5
        wy = (H * 0.5 - y - 0.5)
        if is_wall(rgb):
            verts = [
                bm.verts.new((wx - 0.5, wy - 0.5, 0)),
                bm.verts.new((wx + 0.5, wy - 0.5, 0)),
                bm.verts.new((wx + 0.5, wy + 0.5, 0)),
                bm.verts.new((wx - 0.5, wy + 0.5, 0)),
                bm.verts.new((wx - 0.5, wy - 0.5, WALL_HEIGHT)),
                bm.verts.new((wx + 0.5, wy - 0.5, WALL_HEIGHT)),
                bm.verts.new((wx + 0.5, wy + 0.5, WALL_HEIGHT)),
                bm.verts.new((wx - 0.5, wy + 0.5, WALL_HEIGHT)),
            ]
            for fi in [(0,1,2,3),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]:
                try: bm.faces.new([verts[i] for i in fi])
                except ValueError: pass
        elif is_spawn(rgb):
            spawn_positions.append((wx, wy))

bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.001)
bm.normal_update()
bm.to_mesh(wall_mesh)
bm.free()

for i, (wx, wy) in enumerate(spawn_positions):
    bpy.ops.mesh.primitive_cylinder_add(radius=SPAWN_RADIUS, depth=SPAWN_HEIGHT,
                                        location=(wx, wy, SPAWN_HEIGHT/2))
    sp = bpy.context.active_object
    sp.name = f"Spawn_{{i:02d}}"
    sp.data.materials.append(mat_spawn)

# --- Lighting ---
bpy.ops.object.light_add(type='SUN', location=(20, -20, 40))
sun = bpy.context.active_object
sun.name = "KeyLight"
sun.data.energy = 4.0
sun.data.angle = radians(2.0)
sun.rotation_euler = (radians(50), 0, radians(-30))

world = bpy.context.scene.world
if world is None:
    world = bpy.data.worlds.new("World"); bpy.context.scene.world = world
world.use_nodes = True
bg = world.node_tree.nodes.get("Background")
bg.inputs[0].default_value = (0.55, 0.65, 0.78, 1.0)
bg.inputs[1].default_value = 0.4

# --- Render config ---
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = {samples}
scene.cycles.use_denoising = True
scene.cycles.device = 'CPU'
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
scene.render.film_transparent = False

MAP_SIZE = float(W)

# --- Minimap (ortho top-down) ---
bpy.ops.object.camera_add(location=(0, 0, MAP_SIZE), rotation=(0, 0, 0))
cam_mini = bpy.context.active_object
cam_mini.name = "Cam_Minimap"
cam_mini.data.type = 'ORTHO'
cam_mini.data.ortho_scale = MAP_SIZE * 1.05
scene.camera = cam_mini
scene.render.resolution_x = {minimap_size}
scene.render.resolution_y = {minimap_size}
mini_path = os.path.join(OUT_DIR, f"{{MAP_NAME}}.minimap.png")
scene.render.filepath = mini_path
bpy.ops.render.render(write_still=True)

# --- Thumbnail (3/4 perspective) ---
bpy.ops.object.camera_add(
    location=(MAP_SIZE * 0.85, -MAP_SIZE * 0.85, MAP_SIZE * 0.6),
    rotation=(radians(58), 0, radians(45)))
cam_thumb = bpy.context.active_object
cam_thumb.name = "Cam_Thumb"
cam_thumb.data.lens = 35
cam_thumb.data.clip_start = 0.5
cam_thumb.data.clip_end = 500.0
scene.camera = cam_thumb
scene.render.resolution_x = {thumb_w}
scene.render.resolution_y = {thumb_h}
thumb_path = os.path.join(OUT_DIR, f"{{MAP_NAME}}.thumb.png")
scene.render.filepath = thumb_path
bpy.ops.render.render(write_still=True)

print(f"[render_map] built {{len(spawn_positions)}} spawns, {{len(wall_mesh.vertices)}} wall verts")
print(f"[render_map] minimap -> {{mini_path}}")
print(f"[render_map] thumb   -> {{thumb_path}}")
""".strip()


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--map-dir", required=True,
                    help="Path to a generated map dir (contains <map>.layout.png and .stack.json)")
    ap.add_argument("--minimap-size", type=int, default=512)
    ap.add_argument("--thumb-w", type=int, default=1024)
    ap.add_argument("--thumb-h", type=int, default=768)
    ap.add_argument("--samples", type=int, default=64,
                    help="Cycles samples per pixel (lower = faster, noisier)")
    args = ap.parse_args()

    map_name, layout_png = find_layout_png(args.map_dir)
    out_dir = os.path.abspath(args.map_dir)
    print(f"[render_map] map: {map_name}")
    print(f"[render_map] layout: {layout_png}")
    print(f"[render_map] out_dir: {out_dir}")

    print("[render_map] probing Blender (get_scene_info)")
    info = send("get_scene_info", {})
    if info.get("status") != "success":
        print(f"  ERROR: {info}")
        return 1

    code = build_blender_script(layout_png, out_dir, map_name,
                                args.minimap_size, args.thumb_w, args.thumb_h,
                                args.samples)
    print(f"[render_map] sending execute_code ({len(code)} chars)")
    res = send("execute_code", {"code": code}, timeout=600.0)
    if res.get("status") != "success":
        print(f"  ERROR: {res.get('message') or res}")
        return 2
    result = res.get("result")
    if isinstance(result, dict):
        # MCP wraps stdout under different keys depending on addon version
        result = result.get("output") or result.get("stdout") or json.dumps(result)
    print(f"  -> {(result or '').strip()}")

    mini = os.path.join(out_dir, f"{map_name}.minimap.png")
    thumb = os.path.join(out_dir, f"{map_name}.thumb.png")
    ok = os.path.exists(mini) and os.path.exists(thumb)
    if not ok:
        print(f"[render_map] WARN: expected outputs missing.")
        print(f"  mini exists: {os.path.exists(mini)}")
        print(f"  thumb exists: {os.path.exists(thumb)}")
        return 3

    print(f"[render_map] DONE. minimap={os.path.getsize(mini)} bytes, thumb={os.path.getsize(thumb)} bytes.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
