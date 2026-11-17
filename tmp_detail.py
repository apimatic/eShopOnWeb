import yaml, json
spec=yaml.safe_load(open('finix-spec/finix-openapi.yaml',encoding='utf-8'))
c=spec['components']
def clean(o):
    if isinstance(o,dict):
        return {k:clean(v) for k,v in o.items() if k not in ('examples',)}
    if isinstance(o,list): return [clean(x) for x in o]
    return o
def show(kind,name):
    print('\n\n#####',kind,name,'#####')
    o=(c.get(kind) or {}).get(name)
    print(json.dumps(clean(o),indent=1) if o is not None else 'MISSING')
for n in ['createPaymentInstrumentCard','payment_instrument_card','createAuthorizationMinimalFields','captureAuthorization','voidAuthorization','createTransferReversal','createTransferReversalReferencedRefund','idempotency_id','source','amount','currency','tags']:
    show('schemas',n)
for n in ['Finix-Version','Content-Type','created_at.gte','created_at.lte','limit','after_cursor','before_cursor','idempotency_id','currency','tags.key','tags.value','id','merchant_id']:
    show('parameters',n)
for n in ['createPaymentInstrument','createAuthorization','createTransfer','listTransfers','listAuthorizations','listPaymentInstruments','listFees']:
    show('responses',n)
