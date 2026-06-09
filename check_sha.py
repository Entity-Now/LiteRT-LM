import re
import urllib.request
import hashlib
import json

content = open("WORKSPACE").read()

archives = re.finditer(r'(http_archive|http_jar)\s*\((.*?)\)', content, re.DOTALL)

def get_string_attr(name, text):
    m = re.search(rf'{name}\s*=\s*"([^"]+)"', text)
    if m:
        return m.group(1)
    return None

results = []

for match in archives:
    block = match.group(2)
    name = get_string_attr("name", block)
    sha256 = get_string_attr("sha256", block)
    url = get_string_attr("url", block)
    
    # Check for LITERT_SHA256 etc
    if not sha256:
        m = re.search(r'sha256\s*=\s*([A-Za-z0-9_]+)', block)
        if m:
            var_name = m.group(1)
            # Find var definition in WORKSPACE
            var_m = re.search(rf'{var_name}\s*=\s*"([^"]+)"', content)
            if var_m:
                sha256 = var_m.group(1)
                
    if not url:
        # maybe urls = [...]
        m = re.search(r'urls\s*=\s*\[(.*?)\]', block, re.DOTALL)
        if m:
            urls_block = m.group(1)
            url_m = re.search(r'"([^"]+)"', urls_block)
            if url_m:
                url = url_m.group(1)
        elif "url" not in block:
            m = re.search(r'url\s*=\s*(.+?)\s*,', block)
            if m:
                val = m.group(1)
                # skip complex
                pass
                
    if name and sha256 and url and url.startswith("http"):
        print(f"Checking {name}...")
        try:
            req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
            with urllib.request.urlopen(req) as response:
                data = response.read()
                actual_sha256 = hashlib.sha256(data).hexdigest()
                if actual_sha256 != sha256:
                    print(f"MISMATCH {name}: expected {sha256}, got {actual_sha256} from {url}")
                    results.append({"name": name, "expected": sha256, "actual": actual_sha256, "url": url})
                else:
                    print(f"OK {name}")
        except Exception as e:
            print(f"FAIL {name}: {e}")

if results:
    print("Found mismatches:")
    for r in results:
        print(f"{r['name']}: {r['actual']} (expected {r['expected']})")
else:
    print("All checked checksums are OK!")
