import os
import re
import json
import sys

def parse_tag_manager(project_root):
    tags = []
    layers = {}
    path = os.path.join(project_root, "ProjectSettings", "TagManager.asset")
    if not os.path.exists(path):
        return tags, layers
    
    with open(path, 'r', encoding='utf-8', errors='ignore') as f:
        content = f.read()
        
    # Extract tags
    tag_matches = re.findall(r"tags:\s*\n((?:\s*-\s*.*\n?)*)", content)
    if tag_matches:
        for line in tag_matches[0].split('\n'):
            line = line.strip()
            if line.startswith("- "):
                tags.append(line[2:])
                
    # Extract layers
    # Unity YAML usually lists layers as "User Layer X: Name"
    # Or in TagManager:
    # layers:
    #  - Default
    #  - TransparentFX
    # ...
    # But often it uses 'layers:' list 0-31
    
    layer_matches = re.findall(r"layers:\s*\n((?:\s*-\s*.*\n?)*)", content)
    if layer_matches:
        idx = 0
        for line in layer_matches[0].split('\n'):
            line = line.strip()
            if line.startswith("- "):
                name = line[2:]
                if name:
                    layers[idx] = name
                idx += 1
                
    return tags, layers

def scan_assets(assets_root):
    prefabs = []
    materials = []
    scripts = []
    
    for root, dirs, files in os.walk(assets_root):
        for file in files:
            path = os.path.join(root, file)
            if file.endswith(".prefab"):
                prefabs.append(path)
            elif file.endswith(".mat"):
                materials.append(path)
            elif file.endswith(".cs"):
                scripts.append(path)
                
    return prefabs, materials, scripts

def analyze_prefab(path):
    with open(path, 'r', encoding='utf-8', errors='ignore') as f:
        content = f.read()
        
    info = {
        "name": os.path.basename(path),
        "path": path,
        "components": [],
        "scale_hints": {}
    }
    
    # Find components
    # YAML:  m_Script: {fileID: 11500000, guid: ...}
    # But usually the Type is embedded in the !u! header like --- !u!1 &1001 or !u!65 &...
    # Mapping ID to Type is hard without DLLs, but we can search for common Unity types if they are serialized as standard components
    # e.g. BoxCollider, CharacterController
    
    if "CharacterController" in content:
        info["components"].append("CharacterController")
        # specific height check
        h_match = re.search(r"m_Height:\s*([0-9.]+)", content)
        if h_match:
            info["scale_hints"]["char_height"] = float(h_match.group(1))
        r_match = re.search(r"m_Radius:\s*([0-9.]+)", content)
        if r_match:
            info["scale_hints"]["char_radius"] = float(r_match.group(1))
            
    if "BoxCollider" in content:
        info["components"].append("BoxCollider")
        
    if "CapsuleCollider" in content:
        info["components"].append("CapsuleCollider")
        
    # Tags and Layers
    # m_TagString: Finish
    # m_Layer: 8
    t_match = re.search(r"m_TagString:\s*([^\n\r]+)", content)
    if t_match:
        info["tag"] = t_match.group(1).strip()
        
    l_match = re.search(r"m_Layer:\s*([0-9]+)", content)
    if l_match:
        info["layer"] = int(l_match.group(1))
        
    return info

def analyze_script(path):
    with open(path, 'r', encoding='utf-8', errors='ignore') as f:
        content = f.read()
    
    # Heuristics for scale
    hints = {}
    if "Player" in os.path.basename(path):
        # Look for height constants
        # public const float Height = 1.8f;
        h_match = re.search(r"(?:float|const float)\s+\w*[Hh]eight\s*=\s*([0-9.]+)(?:f)?;", content)
        if h_match:
            hints["script_height"] = float(h_match.group(1))
            
    return hints

def main(uber_root):
    print(f"Scanning {uber_root}...")
    assets_root = os.path.join(uber_root, "Assets")
    
    tags, layers = parse_tag_manager(uber_root)
    
    prefabs_files, mat_files, script_files = scan_assets(assets_root)
    
    print(f"Found {len(prefabs_files)} prefabs, {len(mat_files)} materials, {len(script_files)} scripts.")
    
    # 1. Scale Analysis
    scale_data = []
    # Check ALL prefabs for CharacterController
    for p in prefabs_files:
        # Optimization: only check likely candidates or check first 100 lines? 
        # Actually checking all is fine for 377 prefabs.
        if "Player" in p or "Char" in p or True: 
            data = analyze_prefab(p)
            if "char_height" in data["scale_hints"]:
                scale_data.append(f"Prefab {os.path.basename(p)}: H={data['scale_hints']['char_height']} R={data['scale_hints']['char_radius']}")
    
    # Check scripts
    for s in script_files:
        hints = analyze_script(s)
        if hints:
            scale_data.append(f"Script {os.path.basename(s)}: {hints}")
            
    # 2. Prefab Inventory (Spawn, Teleport, etc.)
    inventory = {
        "Spawn": [],
        "Jump": [],
        "Teleport": [],
        "Pickup": []
    }
    
    vocab_map = {}
    
    for p in prefabs_files:
        name = os.path.basename(p).lower()
        cat = None
        if "spawn" in name: cat = "Spawn"
        elif "jump" in name or "pad" in name: cat = "Jump"
        elif "teleport" in name: cat = "Teleport"
        elif "pickup" in name or "ammo" in name or "health" in name or "armor" in name: cat = "Pickup"
        
        if cat:
            data = analyze_prefab(p)
            inventory[cat].append(data)
            # Add to vocab if it looks definitive
            # e.g. "SpawnPoint.prefab" -> Spawn
            vocab_map[data["name"]] = {"path": p.replace(uber_root, "").replace("\\", "/"), "cat": cat}

    # Material Analysis
    report_lines = [] 
    mat_inventory = []
    
    # Sample a few materials to find generic ones
    for m in mat_files:
        with open(m, 'r', encoding='utf-8', errors='ignore') as f:
            c = f.read()
        
        # Find shader 
        shad = "Unknown"
        s_match = re.search(r"m_Shader:.*type:\s*\d+}\s*#?\s*(.*)", c)
        if s_match:
            shad = s_match.group(1).strip()
            
        # Find textures (m_Texture: ...)
        textures = re.findall(r"m_Texture:.*name:\s*([^\s}]+)", c)
        
        if "Floor" in m or "Wall" in m or "Glass" in m or "Water" in m:
            mat_inventory.append(f"Material {os.path.basename(m)} ({shad}): {', '.join(textures[:3])}...")
            
    # 3. Output Report
    report_lines = []
    report_lines.append("# UberUnity Compatibility Report")
    report_lines.append(f"Source: {uber_root}\n")
    
    report_lines.append("## 1. Scale Analysis")
    if not scale_data:
        report_lines.append("No CharacterController with explicit height found.")
    for l in scale_data: report_lines.append(f"- {l}")
    report_lines.append("")
    
    report_lines.append("## 2. Tags & Layers")
    report_lines.append(f"**Tags**: {', '.join(tags)}")
    report_lines.append("**Layers**:")
    for k, v in layers.items():
        report_lines.append(f"- {k}: {v}")
    report_lines.append("")
    
    report_lines.append("## 3. Gameplay Prefabs")
    for cat, items in inventory.items():
        report_lines.append(f"### {cat}")
        for item in items:
            report_lines.append(f"- **{item['name']}**")
            report_lines.append(f"  - Path: `{item['path']}`")
            report_lines.append(f"  - Tag: {item.get('tag', 'Untagged')}, Layer: {item.get('layer', 0)}")
            report_lines.append(f"  - Components: {', '.join(item['components'])}")
            
    report_lines.append("")
    report_lines.append("## 4. Material Inventory (Sample)")
    for l in mat_inventory[:20]: # Limit output
        report_lines.append(f"- {l}")
    
    with open("UberUnity_Compatibility_Report.md", "w") as f:
        f.write("\n".join(report_lines))
        
    # 4. JSON Vocab
    vocab = {
        "metersPerPixel": 0.2, # Default recommendation
        "wallHeight": 4.0,     # Default
        "prefabs": vocab_map,
        "layers": layers
    }
    
    # Try to derive wallHeight from scale data if possible (heuristic: 2x player height approx)
    # usually 2m player -> 3-4m wall
    
    with open("ubervocab.json", "w") as f:
        json.dump(vocab, f, indent=2)
        
    print("Done.")

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python extractor.py <uber_unity_root>")
        sys.exit(1)
    main(sys.argv[1])
