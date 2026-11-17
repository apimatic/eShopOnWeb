import yaml, json
spec=yaml.safe_load(open('finix-spec/finix-openapi.yaml',encoding='utf-8'))
print(json.dumps(spec.get('servers'),indent=1))
print(json.dumps(spec.get('x-servers'),indent=1))
print(json.dumps(spec.get('info',{}).get('x-logo'),indent=1))
# search strings
import re
txt=open('finix-spec/finix-openapi.yaml',encoding='utf-8').read()
for m in sorted(set(re.findall(r'https?://[^\s"\']+', txt))):
    if 'finix' in m: print(m)
