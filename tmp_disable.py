import yaml, json
spec=yaml.safe_load(open('finix-spec/finix-openapi.yaml',encoding='utf-8'))
c=spec['components']
def clean(o):
    if isinstance(o,dict): return {k:clean(v) for k,v in o.items() if k!='examples'}
    if isinstance(o,list): return [clean(x) for x in o]
    return o
for n in ['updatePaymentInstrumentDisable','updatePaymentInstrumentTags']:
    print('\n#####',n,'#####')
    print(json.dumps(clean(c['schemas'].get(n)),indent=1))
print('\nMETHODS /payment_instruments/{id}:', list(spec['paths']['/payment_instruments/{payment_instrument_id}'].keys()))
print('\nMETHODS /authorizations/{id}:', list(spec['paths']['/authorizations/{authorization_id}'].keys()))
