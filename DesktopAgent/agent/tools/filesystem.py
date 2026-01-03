# tools/filesystem.py
from typing import Optional
def read_file(path:str, **kw)->str:
    """Read a UTF-8 text file."""
    return open(path,"r",encoding="utf-8").read()
def write_file(path:str, content:str, **kw)->str:
    """Write text to a file (UTF-8)."""
    open(path,"w",encoding="utf-8").write(content); return "ok"
