import yaml, json
spec=yaml.safe_load(open('finix-spec/finix-openapi.yaml',encoding='utf-8'))
c=spec['components']
def clean(o):
    if isinstance(o,dict): return {k:clean(v) for k,v in o.items() if k!='examples'}
    if isinstance(o,list): return [clean(x) for x in o]
    return o
for n in ['fee_transfer','fee','page','next','self','createIdentity','identity','address','security_code','capture_amount']:
    print('\n#####',n,'#####')
    print(json.dumps(clean(c['schemas'].get(n)),indent=1))
