import yaml, json
spec=yaml.safe_load(open('finix-spec/finix-openapi.yaml',encoding='utf-8'))
c=spec['components']
def clean(o):
    if isinstance(o,dict): return {k:clean(v) for k,v in o.items() if k!='examples'}
    if isinstance(o,list): return [clean(x) for x in o]
    return o
def show(kind,name):
    print('\n#####',kind,name,'#####')
    o=(c.get(kind) or {}).get(name)
    print(json.dumps(clean(o),indent=1) if o is not None else 'MISSING')
for n in ['transfer','fee','page','next','self']:
    show('schemas',n)
for n in ['createIdentity','identity']:
    show('schemas',n)
for p in ['/identities']:
    print('\n===== PATH',p,'=====')
    for m,op in spec['paths'][p].items():
        if m in ('parameters','servers'): continue
        print(m.upper(),op.get('operationId'))
        print(json.dumps(clean(op.get('requestBody')),indent=1)[:4000])
