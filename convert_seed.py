import base64
import json
import shutil
from pathlib import Path

SOURCE_DIR = Path(r"c:\Users\Shadow\Desktop\uberstrike-mapgen\UberStrikeGen\Assets\_UberStrike\Blueprints\Stacks")
OUT_DIR = Path(r"c:\Users\Shadow\Desktop\uberstrike-mapgen\dataset_seed")

if OUT_DIR.exists():
    shutil.rmtree(OUT_DIR)
OUT_DIR.mkdir()

# Files to process
FILES = [
    "ArenaStack_Sample.layout.png.txt",
    "ArenaStack_Sample.flow.png.txt",
    "ArenaStack_Sample.height.png.txt",
    "ArenaStack_Sample.theme.png.txt",
    "ArenaStack_Sample.lighting.png.txt",
    "ArenaStack_Sample.collision.png.txt",
]

stack_name = "ArenaStack_Sample.stack.json"
shutil.copy(SOURCE_DIR / stack_name, OUT_DIR / "seed.stack.json")

# Update JSON to point to pngs? 
# Our StackIO resolves implicitly if names match stack name.
# So if stack is "seed.stack.json", images should be "seed.layout.png".
# Let's rename them.

for f in FILES:
    src = SOURCE_DIR / f
    if not src.exists():
        print(f"Skipping {f}, not found.")
        continue
        
    with open(src, "r") as txt:
        data = txt.read().strip()
        
    img_data = base64.b64decode(data)
    
    # Extract suffix (e.g. .layout.png.txt -> .layout.png)
    # name is strictly ArenaStack_Sample.TYPE.png.txt
    parts = f.split('.')
    layer_type = parts[1] # layout, flow, etc.
    
    target_name = f"seed.{layer_type}.png"
    
    with open(OUT_DIR / target_name, "wb") as png:
        png.write(img_data)
        
    print(f"Decoded {f} -> {target_name}")

# Inject layer path fields into the stack JSON so BlueprintStack.load() finds them
stack_json_path = OUT_DIR / "seed.stack.json"
with open(stack_json_path, "r") as f:
    stack_data = json.load(f)

layer_types = ["layout", "flow", "height", "theme", "lighting", "collision"]
for lt in layer_types:
    img_name = f"seed.{lt}.png"
    if (OUT_DIR / img_name).exists():
        stack_data[f"{lt}Path"] = img_name

with open(stack_json_path, "w") as f:
    json.dump(stack_data, f, indent=2)

print(f"Seed ready at {stack_json_path}")
