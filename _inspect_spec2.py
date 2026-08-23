import json, sys

def get(spec, ref):
    node = spec
    for p in ref.lstrip("#/").split("/"):
        node = node[p]
    return node

spec_path = sys.argv[1]
spec = json.load(open(spec_path, encoding="utf-8"))
kind = sys.argv[2]
if kind == "path-params":
    path = sys.argv[3]
    method = sys.argv[4]
    op = spec["paths"][path][method]
    print(json.dumps({
        "operationId": op.get("operationId"),
        "parameters": op.get("parameters"),
        "requestBody": op.get("requestBody"),
        "responses": {k: {"description": v.get("description"), "schema": (v.get("content") or {}).get("application/json", {}).get("schema")} for k, v in op.get("responses", {}).items()}
    }, indent=2)[:15000])
elif kind == "schema":
    name = sys.argv[3]
    print(json.dumps(spec["components"]["schemas"][name], indent=2)[:20000])
elif kind == "desc":
    name = sys.argv[3]
    s = spec["components"]["schemas"][name]
    print("title:", s.get("title"))
    print("desc:", s.get("description", "")[:2000])
    print("required:", s.get("required"))
    print("props:", list((s.get("properties") or {}).keys()))
    for pn, p in (s.get("properties") or {}).items():
        d = (p.get("description") or "")[:200]
        print(f"  {pn}: type={p.get('type')} enum={p.get('enum')} ref={p.get('$ref')} desc={d}")
