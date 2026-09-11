using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

var apiKey = Environment.GetEnvironmentVariable("MAXIO_API_KEY")!;
var subdomain = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN")!;
var familyHandle = Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY")!;

Console.WriteLine($"Subdomain: {subdomain}");
Console.WriteLine($"Family: {familyHandle}");
Console.WriteLine($"ApiKey present: {apiKey.Length > 0}");

var baseUrl = $"https://{subdomain}.chargify.com/";
var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
var creds = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:x"));
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

Console.WriteLine("\n=== Test 1: Get product families ===");
var pfResp = await client.GetAsync("/product_families.json");
Console.WriteLine($"Status: {pfResp.StatusCode}");
var pfJson = await pfResp.Content.ReadAsStringAsync();
Console.WriteLine($"Body length: {pfJson.Length}");
Console.WriteLine($"Body: {pfJson.Substring(0, Math.Min(200, pfJson.Length))}");

Console.WriteLine("\n=== Test 2: Get products ===");
var prodResp = await client.GetAsync("/products.json");
Console.WriteLine($"Status: {prodResp.StatusCode}");
var prodJson = await prodResp.Content.ReadAsStringAsync();
Console.WriteLine($"Body length: {prodJson.Length}");
Console.WriteLine($"Body: {prodJson.Substring(0, Math.Min(300, prodJson.Length))}");

// Try deserializing
Console.WriteLine("\n=== Test 3: Deserialize products ===");
var envelopes = JsonSerializer.Deserialize<List<ProductEnvelope>>(prodJson, opts);
Console.WriteLine($"Deserialized count: {envelopes?.Count ?? 0}");
if (envelopes != null)
{
    foreach (var e in envelopes)
    {
        Console.WriteLine($"  Product: {e.Product?.Name} (handle={e.Product?.Handle}, family={e.Product?.ProductFamily?.Handle})");
    }
}

// Test customer lookup
Console.WriteLine("\n=== Test 4: Customer lookup ===");
var custResp = await client.GetAsync("/customers/lookup.json?reference=test-user-id-123");
Console.WriteLine($"Status: {custResp.StatusCode}");

Console.WriteLine("\nDone!");

class ProductEnvelope
{
    [JsonPropertyName("product")]
    public ProductData? Product { get; set; }
}

class ProductData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Handle { get; set; } = "";
    [JsonPropertyName("product_family")]
    public FamilyData? ProductFamily { get; set; }
}

class FamilyData
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
}
