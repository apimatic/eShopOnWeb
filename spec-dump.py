import json, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

def load(p):
    with open(p, encoding='utf-8', errors='replace') as f:
        return json.load(f)

base = 'api-specs/paypal/'
name = sys.argv[1]
d = load(base + name + '/' + name + '.json')
schemas = d['components']['schemas']

def resolve(s):
    while isinstance(s, dict) and '$ref' in s:
        s = schemas[s['$ref'].split('/')[-1]]
    return s

def merge_allof(s):
    if s is None:
        return {}
    s = dict(s)
    props = {}
    req = []
    for sub in s.pop('allOf', []) or []:
        sub = merge_allof(resolve(sub))
        props.update(sub.get('properties', {}))
        req += sub.get('required', [])
    props.update(s.get('properties', {}))
    req += s.get('required', [])
    if props:
        s['properties'] = props
        s['required'] = sorted(set(req))
        s['type'] = 'object'
    return s

def summarize(name, s, depth=0, maxdepth=4):
    s = merge_allof(resolve(s))
    pad = '  ' * depth
    t = s.get('type')
    if t == 'object' or 'properties' in s:
        req = set(s.get('required', []))
        print(f'{pad}{name}: object required={sorted(req)}')
        if depth < maxdepth:
            for pn, ps in s.get('properties', {}).items():
                summarize(pn, ps, depth + 1, maxdepth)
    elif t == 'array':
        print(f'{pad}{name}: array of:')
        if depth < maxdepth:
            summarize(name + '[]', s.get('items', {}), depth + 1, maxdepth)
    else:
        enum = s.get('enum')
        desc = (s.get('description') or '')[:90].replace('\n', ' ')
        print(f'{pad}{name}: {t} enum={enum} {desc}')

for target in sys.argv[2:]:
    if target not in schemas:
        print('MISSING SCHEMA:', target)
        print('  candidates:', [k for k in schemas if target.lower() in k.lower()][:20])
        continue
    print('=' * 60)
    summarize(target, schemas[target])
