import os

search_str = "from agent_v2.blueprints.stack_io import BlueprintStack"
root_dir = "DesktopAgent"

with open("import_scan.log", "w") as log:
    for root, dirs, files in os.walk(root_dir):
        for file in files:
            if file.endswith(".py"):
                path = os.path.join(root, file)
                try:
                    with open(path, "r", encoding="utf-8") as f:
                        content = f.read()
                        if search_str in content:
                            status = "TYPE_CHECKING" if "if TYPE_CHECKING:" in content else "DIRECT"
                            log.write(f"{status}: {path}\n")
                except Exception as e:
                    print(f"Error reading {path}: {e}")
