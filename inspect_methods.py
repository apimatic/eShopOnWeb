import yaml
with open('maxio-spec/openapi.yaml', encoding='utf-8') as f:
    spec = yaml.safe_load(f)
paths = spec.get('paths', {})
for path in ['/customers.json','/subscriptions.json','/product_families.json','/products.json','/subscription_groups/signup.json']:
    print('===', path, '===')
    info = paths.get(path, {})
    for method, detail in info.items():
        if isinstance(detail, dict) and 'operationId' in detail:
            print(method.upper(), detail.get('operationId'), detail.get('summary'))
