import yaml
with open('maxio-spec/openapi.yaml', encoding='utf-8') as f:
    spec = yaml.safe_load(f)

def get_ref(path, method):
    return spec['paths'][path][method]

def show_schema(name, schema):
    if isinstance(schema, dict):
        if '$ref' in schema:
            ref = schema['$ref'].split('/')[-1]
            s = spec['components']['schemas'].get(ref, {})
            print(name, '->', ref, 'properties:', list(s.get('properties', {}).keys())[:30])
            return
        props = schema.get('properties', {})
        print(name, 'props:', list(props.keys())[:30])
        req = schema.get('required', [])
        print('  required:', req)
    else:
        print(name, schema)

for path, method in [('/customers.json','post'),('/subscriptions.json','post'),('/subscription_groups/signup.json','post')]:
    op = get_ref(path, method)
    req = op.get('requestBody',{}).get('content',{}).get('application/json',{}).get('schema',{})
    print('===', path, method, '===')
    show_schema('request', req)
    resp = op.get('responses',{}).get('200',{}).get('content',{}).get('application/json',{}).get('schema',{})
    show_schema('response200', resp)
