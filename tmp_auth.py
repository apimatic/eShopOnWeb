import yaml, json
spec=yaml.safe_load(open('finix-spec/finix-openapi.yaml',encoding='utf-8'))
a=spec['components']['schemas']['authorization']
print(sorted(a['properties'].keys()))
print('\nAUTH fee/transfer/amount props:')
for k in ['fee','transfer','amount','amount_requested','state','is_void','void_state','expires_at','merchant','source','id','currency','failure_code','failure_message','tags','idempotency_id','network_details']:
    print(k, '=>', json.dumps(a['properties'].get(k),indent=1)[:800])
