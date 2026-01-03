# tools/web.py
import requests, re
ALLOWLIST = ["https://example.com"]
def http_get(url:str, **kw)->str:
    """HTTP GET (allowlisted)."""
    if not any(url.startswith(a) for a in ALLOWLIST): return "blocked"
    r=requests.get(url,timeout=20); r.raise_for_status(); 
    return r.text[:5000]
