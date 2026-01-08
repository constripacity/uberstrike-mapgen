import os
import re
import math
import sys

def parse_yaml_array(content, key):
    # Matches simple YAML arrays like:
    # m_Center: {x: 0, y: 0, z: 0}
    # m_Extents: {x: 0.5, y: 0.5, z: 0.5} (BoxCollider)
    # m_Size: {x: 1, y: 1, z: 1} (rare in raw YAML, usually extents)
    regex = rf"{key}:\s*{{x:\s*([0-9.-]+),\s*y:\s*([0-9.-]+),\s*z:\s*([0-9.-]+)}}"
    m = re.search(regex, content)
    if m:
        return float(m.group(1)), float(m.group(2)), float(m.group(3))
    return None

def get_mesh_filter_scale(content):
    # Try to find user-set local scale on Transform
    # m_LocalScale: {x: 1, y: 1, z: 1}
    # Note: A prefab can have multiple GameObjects. We need to associate Transform with Renderer/Collider.
    # This parser is heuristic and simplistic (assumes single main object or uniform scale).
    regex = r"m_LocalScale:\s*{x:\s*([0-9.-]+),\s*y:\s*([0-9.-]+),\s*z:\s*([0-9.-]+)}"
    m = re.search(regex, content)
    if m:
        return float(m.group(1)), float(m.group(2)), float(m.group(3))
    return 1.0, 1.0, 1.0

def scan_bounds(uber_root, vocab_file):
    # Read vocab to get the list of interesting prefabs
    import json
    with open(vocab_file, 'r') as f:
        vocab = json.load(f)
    
    interesting_paths = []
    for name, info in vocab.get("prefabs", {}).items():
        rel = info.get("path")
        if rel:
            # Handle potential leading slashes
            if rel.startswith("/") or rel.startswith("\\"): rel = rel[1:]
            full = os.path.join(uber_root, rel)
            interesting_paths.append(full)

    results = []
    
    print(f"Scanning {len(interesting_paths)} prefabs for bounds...")

    for path in interesting_paths:
        if not os.path.exists(path):
            continue
            
        with open(path, 'r', encoding='utf-8', errors='ignore') as f:
            content = f.read()
            
        name = os.path.basename(path)
        
        # Heuristic 1: BoxCollider
        # m_Size: {x: ..., y: ..., z: ...} property on BoxCollider
        # Actually serialized as m_Size usually
        box_size = parse_yaml_array(content, "m_Size")
        
        # Heuristic 2: CharacterController
        # m_Height, m_Radius
        cc_height = None
        m_h = re.search(r"m_Height:\s*([0-9.]+)", content)
        if m_h: cc_height = float(m_h.group(1))
        
        # Determine strict bounds
        size_x, size_y, size_z = 0, 0, 0
        source = "None"
        
        # Get Transform Scale (rough global application)
        sx, sy, sz = get_mesh_filter_scale(content)
        
        if box_size:
            size_x, size_y, size_z = box_size[0] * sx, box_size[1] * sy, box_size[2] * sz
            source = "BoxCollider"
        elif cc_height:
            size_y = cc_height * sy
            size_x = size_z = 1.0 * sx # Approximate width
            source = "CharController"
        else:
            # Fallback: Maybe MeshRenderer bounds? Hard to know without mesh data.
            # Assume 1x1x1 if nothing found but it's a known prefab
            source = "Unknown"
        
        results.append(f"{name},{size_x:.3f},{size_y:.3f},{size_z:.3f},{source}")

    # Output CSV
    out_path = os.path.join(os.path.dirname(vocab_file), "prefab_bounds.csv")
    with open(out_path, 'w') as f:
        f.write("Name,SizeX,SizeY,SizeZ,Source\n")
        for r in results:
            f.write(r + "\n")
            
    print(f"Wrote bounds to {out_path}")
    
    # Validation Check
    # Check if JumpPads are huge or tiny
    # Check if Pickups are tiny
    pass

if __name__ == "__main__":
    if len(sys.argv) < 3:
        print("Usage: python bounds_sampler.py <uber_root> <vocab_json>")
        sys.exit(1)
    scan_bounds(sys.argv[1], sys.argv[2])
