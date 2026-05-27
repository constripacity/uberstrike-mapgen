"""Lightweight collaborative map editor backend."""
from __future__ import annotations

import base64
import json
import subprocess
import sys
from pathlib import Path
from typing import Dict

from flask import Flask, abort, jsonify, render_template, request, send_file
from flask_socketio import SocketIO, emit
from PIL import Image
from werkzeug.utils import safe_join

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from agent_v2.generator.layer_generator import AILayerGenerator  # noqa: E402
from agent_v2.validator.stack_validator import StackValidator  # noqa: E402

app = Flask(__name__, template_folder="templates")
socketio = SocketIO(
    app,
    cors_allowed_origins=["http://127.0.0.1:5000", "http://localhost:5000"],
)


@app.before_request
def _localhost_only() -> None:
    # Hard floor: refuse any request whose source isn't loopback, regardless of
    # how the server was started. Protects against accidental 0.0.0.0 binds.
    if request.remote_addr not in ("127.0.0.1", "::1"):
        abort(403)

STACK_DIR = Path("UberStrikeGen/Assets/_UberStrike/Blueprints/Stacks")
STACK_DIR.mkdir(parents=True, exist_ok=True)
_STACK_DIR_RESOLVED = STACK_DIR.resolve()


def _safe_stack_path(name: str | None) -> Path | None:
    """Resolve `name` against STACK_DIR. Returns None if name is empty or attempts
    to escape STACK_DIR (absolute path, .. segments, symlink-out, etc.)."""
    if not name or not isinstance(name, str):
        return None
    joined = safe_join(str(STACK_DIR), name)
    if joined is None:
        return None
    candidate = Path(joined)
    try:
        resolved = candidate.resolve()
    except (OSError, RuntimeError):
        return None
    try:
        resolved.relative_to(_STACK_DIR_RESOLVED)
    except ValueError:
        return None
    return candidate

def _image_to_base64(img: Image.Image) -> str:
    buffer = Path("DesktopAgent/temp")
    buffer.mkdir(parents=True, exist_ok=True)
    tmp = buffer / "preview.png"
    img.save(tmp, format="PNG")
    data = tmp.read_bytes()
    tmp.unlink(missing_ok=True)
    return base64.b64encode(data).decode("ascii")


def _load_stack_images(stack_path: Path) -> Dict[str, Image.Image]:
    layers: Dict[str, Image.Image] = {}
    base = stack_path.with_suffix("")
    for suffix in ("layout", "height", "flow", "theme", "lighting", "collision"):
        candidate = base.with_suffix(f".{suffix}.png")
        if candidate.exists():
            layers[suffix] = Image.open(candidate)
    return layers


@app.route("/")
def index() -> str:
    return render_template("map_editor.html")


@app.route("/api/maps")
def list_maps():
    stacks = sorted(STACK_DIR.glob("*.stack.json"))
    return jsonify([stack.name for stack in stacks])


@app.route("/api/load", methods=["POST"])
def load_stack():
    payload = request.get_json(force=True)
    stack_name = payload.get("name")
    stack_path = _safe_stack_path(stack_name)
    if stack_path is None or not stack_path.exists():
        return jsonify({"error": "stack not found"}), 404

    layers = {name: _image_to_base64(img) for name, img in _load_stack_images(stack_path).items()}
    return jsonify({"stack": stack_name, "layers": layers, "path": str(stack_path)})


@app.route("/api/generate", methods=["POST"])
def generate_map():
    payload = request.get_json(force=True)
    prompt = payload.get("prompt", "arena")

    generator = AILayerGenerator()
    stack_path = Path(generator.generate_from_prompt(prompt))
    layers = {name: _image_to_base64(img) for name, img in _load_stack_images(stack_path).items()}
    return jsonify({"stack": stack_path.name, "layers": layers, "path": str(stack_path)})


@app.route("/api/build", methods=["POST"])
def build_map():
    payload = request.get_json(force=True)
    stack_path = payload.get("path")
    if not stack_path:
        return jsonify({"error": "path missing"}), 400

    cmd = ["python", "run_assistant.py", "monitor"]
    process = subprocess.Popen(cmd, cwd=Path(__file__).resolve().parents[1])  # non-blocking
    return jsonify({"success": True, "pid": process.pid})


@app.route("/api/validate", methods=["POST"])
def validate_stack():
    payload = request.get_json(force=True)
    stack_path = payload.get("path")
    if not stack_path:
        return jsonify({"error": "path missing"}), 400

    validator = StackValidator()
    result = validator.validate_stack(stack_path)
    return jsonify(result)


@app.route("/preview/<path:filename>")
def preview(filename: str):
    target = _safe_stack_path(filename)
    if target is None or not target.exists():
        return ("Not Found", 404)
    return send_file(target)


_ALLOWED_LAYERS = {"layout", "height", "flow", "theme", "lighting", "collision"}


@socketio.on("edit_pixel")
def handle_edit_pixel(data):
    raw_stack_path = data.get("stack_path", "")
    # Accept only stack paths under STACK_DIR. Reject absolute paths, .., or
    # anything resolving outside the trusted root, and only allow known layer
    # suffixes -- prevents pixel-write to arbitrary files via the websocket.
    try:
        rel = Path(raw_stack_path).relative_to(STACK_DIR)
    except ValueError:
        rel = Path(raw_stack_path) if not Path(raw_stack_path).is_absolute() else None
    stack_path = _safe_stack_path(str(rel)) if rel is not None else None

    layer = data.get("layer")
    x = data.get("x")
    y = data.get("y")
    color = tuple(data.get("color", [255, 255, 255, 255]))

    if stack_path is None or not stack_path.exists() or layer not in _ALLOWED_LAYERS:
        emit("error", {"message": "invalid edit request"})
        return

    image_path = stack_path.with_suffix(f".{layer}.png")
    if not image_path.exists():
        emit("error", {"message": "layer missing"})
        return

    img = Image.open(image_path).convert("RGBA")
    pixels = img.load()
    pixels[int(x), int(y)] = tuple(int(c) for c in color)
    img.save(image_path)

    emit("pixel_updated", data, broadcast=True)


if __name__ == "__main__":
    socketio.run(app, host="127.0.0.1", port=5000, debug=False)
