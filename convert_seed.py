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

# We also need to update the json if it has hardcoded paths?
# The source json didn't have paths.
# But StackIO.load() tries to find implicitly.
# If we name them seed.layout.png and verify seed.stack.json loads, we are good.

print(f"Seed ready at {OUT_DIR / 'seed.stack.json'}")
