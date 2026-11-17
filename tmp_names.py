import yaml, json
spec=yaml.safe_load(open('finix-spec/finix-openapi.yaml',encoding='utf-8'))
c=spec['components']
names=list(c['schemas'].keys())
for kw in ['ayment','uthoriz','ransfer','ee','efund','eversal','age']:
    print('---',kw,'---')
    print('\n'.join(n for n in names if kw.lower() in n.lower()))
