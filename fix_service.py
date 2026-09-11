with open('src/PublicApi/Maxio/MaxioService.cs', encoding='utf-8') as f:
    content = f.read()
old1 = 'await _http.PostAsJsonAsync("/customers.json", payload);'
new1 = 'var customerContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");\n        var resp = await _http.PostAsync("/customers.json", customerContent);'
content = content.replace(old1, new1)
old2 = 'var resp = await _http.PostAsJsonAsync("/subscriptions.json", payload);'
new2 = 'var subContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");\n        var resp = await _http.PostAsync("/subscriptions.json", subContent);'
content = content.replace(old2, new2)
with open('src/PublicApi/Maxio/MaxioService.cs', 'w', encoding='utf-8') as f:
    f.write(content)
print('done')
