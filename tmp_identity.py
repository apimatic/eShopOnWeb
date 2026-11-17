import yaml, json
spec=yaml.safe_load(open('finix-spec/finix-openapi.yaml',encoding='utf-8'))
c=spec['components']
def clean(o):
    if isinstance(o,dict): return {k:clean(v) for k,v in o.items() if k!='examples'}
    if isinstance(o,list): return [clean(x) for x in o]
    return o
op=spec['paths']['/identities']['post']
print('OP',op.get('operationId'))
print(json.dumps(clean(op.get('requestBody')),indent=1)[:3000])
print('RESP',json.dumps(clean(op.get('responses')),indent=1)[:1000])
names=[n for n in c['schemas'] if 'identity' in n.lower() or n.lower().startswith('entity') or n.lower() in ('entity_personal','entity_business')]
print(names)
for n in ['createIdentityPersonal','createIdentityBusiness','entity_personal','entity','identity_roles']:
    if n in c['schemas']:
        print('\n#####',n,'#####')
        print(json.dumps(clean(c['schemas'][n]),indent=1)[:6000])
