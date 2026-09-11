with open('src/Infrastructure/Services/MaxioBillingService.cs') as f:
    content = f.read()
# Replace all GetValue<int>() with Int32.Parse(...ToString()) pattern manually targeted
# Use simple replacements for common patterns
content = content.replace('item["id"]?.GetValue<int>() ?? 0', 'int.Parse(item["id"]?.ToString() ?? "0")')
content = content.replace('item["handle"]?.GetValue<string>() ?? string.Empty', 'item["handle"]?.ToString() ?? string.Empty')
content = content.replace('item["name"]?.GetValue<string>() ?? string.Empty', 'item["name"]?.ToString() ?? string.Empty')
content = content.replace('item["price_in_cents"]!.GetValue<decimal>() / 100m', 'decimal.Parse(item["price_in_cents"]?.ToString() ?? "0") / 100m')
content = content.replace('familyHandle = item["product_family"]?["handle"]?.GetValue<string>() ?? string.Empty', 'familyHandle = item["product_family"]?["handle"]?.ToString() ?? string.Empty')
content = content.replace('item["product_handle"]?.GetValue<string>() ?? string.Empty', 'item["product_handle"]?.ToString() ?? string.Empty')
content = content.replace('item["state"]?.GetValue<string>() ?? string.Empty', 'item["state"]?.ToString() ?? string.Empty')
content = content.replace('item["next_billing_date"]!.GetValue<string>()', 'item["next_billing_date"]?.ToString() ?? string.Empty')
content = content.replace('item["product_price_in_cents"]!.GetValue<decimal>() / 100m', 'decimal.Parse(item["product_price_in_cents"]?.ToString() ?? "0") / 100m')
content = content.replace('item["customer"]["reference"]?.GetValue<string>() ?? string.Empty', 'item["customer"]?["reference"]?.ToString() ?? string.Empty')
content = content.replace('customer.GetValue<int>("id")', 'int.Parse(customer["id"]?.ToString() ?? "0")')
content = content.replace('root?["id"]?.GetValue<int>() ?? 0', 'int.Parse(root?["id"]?.ToString() ?? "0")')
content = content.replace('node["id"]?.GetValue<int>() ?? 0', 'int.Parse(node["id"]?.ToString() ?? "0")')
content = content.replace('node["customer"]["reference"]?.GetValue<string>() ?? string.Empty', 'node["customer"]?["reference"]?.ToString() ?? string.Empty')
content = content.replace('node["product_handle"]?.GetValue<string>() ?? string.Empty', 'node["product_handle"]?.ToString() ?? string.Empty')
content = content.replace('node["state"]?.GetValue<string>() ?? string.Empty', 'node["state"]?.ToString() ?? string.Empty')
content = content.replace('DateTime.Parse(node["next_billing_date"]!.GetValue<string>())', 'DateTime.Parse(node["next_billing_date"]?.ToString() ?? "")')
content = content.replace('node["product_price_in_cents"]!.GetValue<decimal>() / 100m', 'decimal.Parse(node["product_price_in_cents"]?.ToString() ?? "0") / 100m')
with open('src/Infrastructure/Services/MaxioBillingService.cs','w') as f:
    f.write(content)
