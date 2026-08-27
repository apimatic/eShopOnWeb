import json, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

def load(p):
    with open(p, encoding='utf-8', errors='replace') as f:
        return json.load(f)

base = 'api-specs/paypal/'
name = sys.argv[1]
d = load(base + name + '/' + name + '.json')
print('==', name, d.get('openapi'), d['info'].get('title'), d['info'].get('version'))
print('SERVERS:', json.dumps(d.get('servers')))
print('SECURITY:', json.dumps(d.get('security')))
print('SECSCHEMES:', json.dumps(d.get('components', {}).get('securitySchemes')))
print('PATHS:')
for p, ops in d['paths'].items():
    for m, op in ops.items():
        if m in ('get','post','put','patch','delete'):
            params = [pr.get('name') for pr in op.get('parameters', []) if isinstance(pr, dict)]
            print(f'  {m.upper()} {p}  opId={op.get("operationId")} params={params}')
