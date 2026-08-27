import json, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

def load(p):
    with open(p, encoding='utf-8', errors='replace') as f:
        return json.load(f)

base = 'api-specs/paypal/'
name = sys.argv[1]
d = load(base + name + '/' + name + '.json')
schemas = d['components']['schemas']

def resolve(s, depth=0):
    while isinstance(s, dict) and '$ref' in s:
        s = schemas[s['$ref'].split('/')[-1]]
    return s

def summarize(name, s, depth=0, maxdepth=3):
    s = resolve(s)
    if not isinstance(s, dict):
        return
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
        desc = (s.get('description') or '')[:100].replace('\n', ' ')
        print(f'{pad}{name}: {t} enum={enum} {desc}')

for target in sys.argv[2:]:
    if target not in schemas:
        print('MISSING SCHEMA:', target)
        # fuzzy
        print('  candidates:', [k for k in schemas if target.lower() in k.lower()][:20])
        continue
    print('=' * 60)
    summarize(target, schemas[target])
