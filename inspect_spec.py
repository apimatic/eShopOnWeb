import yaml
with open('maxio-spec/openapi.yaml', encoding='utf-8') as f:
    spec = yaml.safe_load(f)
paths = list(spec.get('paths', {}).keys())
for p in paths:
    if any(x in p for x in ['customers','subscriptions','products','components','product_families']):
        print(p)
