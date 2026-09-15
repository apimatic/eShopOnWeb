import json
p = r"api-specs/paypal/transaction_search_v1/transaction_search_v1.json"
with open(p, encoding="utf-8") as f:
    spec = json.load(f)
op = spec["paths"]["/v1/reporting/transactions"]["get"]
print("summary:", op.get("summary"))
print("operationId:", op.get("operationId"))
for param in op.get("parameters", []):
    sch = param.get("schema", {})
    print(f"PARAM {param.get('name')} in={param.get('in')} req={param.get('required')} type={sch.get('type')} max={sch.get('maximum')} default={sch.get('default')}")
    print("  ", (param.get("description") or "")[:300])
print("RESPONSES", list(op["responses"]))
schemas = spec["components"]["schemas"]
print("SCHEMAS", list(schemas))
for name, s in schemas.items():
    print("=" * 20, name, "=" * 20)
    print(json.dumps(s, indent=2)[:2500])
