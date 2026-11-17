import yaml, json
spec=yaml.safe_load(open('finix-spec/finix-openapi.yaml',encoding='utf-8'))
def clean(o):
    if isinstance(o,dict):
        return {k:clean(v) for k,v in o.items() if k not in ('examples','example','description')}
    if isinstance(o,list): return [clean(x) for x in o]
    return o
for p in ['/payment_instruments','/payment_instruments/{payment_instrument_id}','/authorizations','/authorizations/{authorization_id}','/authorizations/{authorization_id_void_to}','/transfers','/transfers/{transfer_id}/reversals','/transfers/{transfer_id}']:
    print('\n=====',p,'=====')
    for m,op in spec['paths'][p].items():
        if m in ('parameters','servers'): continue
        print(' METHOD',m.upper(),op.get('operationId'))
        print(' params:',json.dumps(clean(op.get('parameters')),indent=0)[:2500])
        print(' body:',json.dumps(clean(op.get('requestBody')),indent=0)[:2000])
        print(' responses:',json.dumps(clean(op.get('responses')),indent=0)[:1200])
