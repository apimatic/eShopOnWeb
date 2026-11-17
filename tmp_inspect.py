import yaml, json
spec=yaml.safe_load(open('finix-spec/finix-openapi.yaml',encoding='utf-8'))
c=spec['components']
print('SERVERS', json.dumps(spec.get('servers'),indent=1))
print('SEC', json.dumps(spec.get('security'),indent=1))
print('SECSCHEMES', json.dumps(c.get('securitySchemes'),indent=1))
for n in ['createPaymentInstrument','payment_instrument','createAuthorization','authorization','captureAuthorization','voidAuthorization','createTransferReversal','transfer','fee','createTransferMinimalFields']:
    print('\n\n##### SCHEMA',n,'#####')
    s=c['schemas'].get(n)
    print(json.dumps(s,indent=1) if s else 'MISSING')
