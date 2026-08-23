import json
import sys

def resolve(spec, ref, seen=None):
    if seen is None:
        seen = set()
    if not isinstance(ref, dict):
        return ref
    if "$ref" in ref:
        path = ref["$ref"]
        if path in seen:
            return {"$ref": path, "_circular": True}
        seen.add(path)
        node = spec
        for p in path.lstrip("#/").split("/"):
            node = node[p]
        return resolve(spec, node, seen)
    out = {}
    for k, v in ref.items():
        if k in ("example", "examples", "x-enum-descriptions"):
            continue
        if isinstance(v, dict):
            out[k] = resolve(spec, v, seen)
        elif isinstance(v, list):
            out[k] = [resolve(spec, i, seen) if isinstance(i, dict) else i for i in v]
        else:
            out[k] = v
    return out


def schema_summary(s, indent=0, depth=0, maxdepth=4):
    if depth > maxdepth:
        return "  " * indent + "...\n"
    lines = []
    pref = "  " * indent
    if not isinstance(s, dict):
        return pref + str(s) + "\n"
    if s.get("_circular"):
        return pref + s["$ref"] + " (circular)\n"
    t = s.get("type")
    if "allOf" in s:
        lines.append(pref + "allOf:\n")
        for i in s["allOf"]:
            lines.append(schema_summary(i, indent + 1, depth + 1, maxdepth))
    if "oneOf" in s:
        lines.append(pref + "oneOf:\n")
        for i in s["oneOf"]:
            lines.append(schema_summary(i, indent + 1, depth + 1, maxdepth))
    if "anyOf" in s:
        lines.append(pref + "anyOf:\n")
        for i in s["anyOf"]:
            lines.append(schema_summary(i, indent + 1, depth + 1, maxdepth))
    if t == "object" or "properties" in s:
        req = s.get("required", [])
        props = s.get("properties", {})
        extra = {k: s[k] for k in ("additionalProperties", "minProperties") if k in s}
        lines.append(pref + f"object required={req} {extra}\n")
        for name, ps in props.items():
            r = "*" if name in req else " "
            desc = (ps.get("description") or "")[:100].replace("\n", " ")
            lines.append(pref + f"  {r}{name}: type={ps.get('type')} enum={ps.get('enum')} {desc}\n")
            if ps.get("properties") or ps.get("allOf") or ps.get("oneOf") or "$ref" in ps:
                lines.append(schema_summary(ps, indent + 2, depth + 1, maxdepth))
            elif ps.get("type") == "array" and isinstance(ps.get("items"), dict):
                lines.append(pref + "    items:\n")
                lines.append(schema_summary(ps["items"], indent + 3, depth + 1, maxdepth))
    elif t == "array":
        lines.append(pref + "array\n")
        if isinstance(s.get("items"), dict):
            lines.append(schema_summary(s["items"], indent + 1, depth + 1, maxdepth))
    elif "enum" in s:
        lines.append(pref + f"type={t} enum={s['enum']}\n")
    else:
        extra = {k: s[k] for k in s if k not in ("description", "type")}
        lines.append(pref + f"type={t} {extra if extra else ''}\n")
    return "".join(lines)


def dump_op(spec, path, method):
    op = spec["paths"][path][method]
    print(f"\n===== {method.upper()} {path} ({op.get('operationId')}) =====")
    params = op.get("parameters", [])
    print("params:")
    for p in params:
        print(f"  {p.get('in')} {p.get('name')} required={p.get('required')} schema={p.get('schema')}")
    rb = op.get("requestBody")
    if rb:
        content = rb.get("content", {})
        for ct, body in content.items():
            print(f"requestBody content={ct} required={rb.get('required')}")
            sch = body.get("schema", {})
            if "$ref" in sch:
                print("  ref:", sch["$ref"])
                name = sch["$ref"].split("/")[-1]
                if name in spec["components"]["schemas"]:
                    print(schema_summary(resolve(spec, spec["components"]["schemas"][name]), maxdepth=4)[:12000])
            else:
                print(schema_summary(resolve(spec, sch), maxdepth=4)[:8000])
    print("responses:")
    for code, resp in op.get("responses", {}).items():
        desc = (resp.get("description") or "")[:120]
        content = resp.get("content", {})
        for ct, body in content.items():
            sch = body.get("schema", {})
            ref = sch.get("$ref") if isinstance(sch, dict) else None
            print(f"  {code} {ct} ref={ref} desc={desc}")


if __name__ == "__main__":
    spec_path = sys.argv[1]
    spec = json.load(open(spec_path, encoding="utf-8"))
    print("SCHEMAS:", sorted(spec["components"]["schemas"].keys()))
    if len(sys.argv) > 2:
        if sys.argv[2] == "--ops":
            dump_op(spec, sys.argv[3], sys.argv[4])
        elif sys.argv[2] == "--schema":
            name = sys.argv[3]
            print("====", name, "====")
            print(schema_summary(resolve(spec, spec["components"]["schemas"][name]), maxdepth=int(sys.argv[4]) if len(sys.argv) > 4 else 4)[:20000])
        elif sys.argv[2] == "--headers":
            # dump all ops' headers
            for path, item in spec["paths"].items():
                for method, op in item.items():
                    if method not in ("get", "post", "put", "patch", "delete"):
                        continue
                    params = [p for p in op.get("parameters", []) if p.get("in") == "header"]
                    print(f"{method.upper()} {path}: headers={[p['name'] for p in params]}")
