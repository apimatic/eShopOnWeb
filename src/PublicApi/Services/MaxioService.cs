using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioService : IMaxioService
{
    private readonly HttpClient _client;
    private readonly MaxioSettings _settings;

    public MaxioService(IOptions<MaxioSettings> options)
    {
        _settings = options.Value;
        _client = new HttpClient();
        var baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? $"https://{_settings.Subdomain}.chargify.com"
            : _settings.BaseUrl.TrimEnd('/');
        _client.BaseAddress = new Uri(baseUrl + "/");
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }
    public async Task<JsonObject> FindOrCreateCustomerAsync(string reference, string email, string firstName = "", string lastName = "")
    {
        try
        {
            var resp = await _client.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
            if (resp.IsSuccessStatusCode)
            {
                var doc = await resp.Content.ReadAsStringAsync();
                var node = JsonNode.Parse(doc) as JsonObject;
                if (node != null && node["customer"] != null)
                    return node["customer"]!.AsObject();
            }
        }
        catch { }
        var payload = new JsonObject
        {
            ["customer"] = new JsonObject
            {
                ["reference"] = reference,
                ["email"] = email,
                ["first_name"] = firstName,
                ["last_name"] = lastName
            }
        };
        var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        var createResp = await _client.PostAsync("customers.json", content);
        createResp.EnsureSuccessStatusCode();
        var createDoc = await createResp.Content.ReadAsStringAsync();
        var createNode = JsonNode.Parse(createDoc) as JsonObject;
        return createNode?["customer"]?.AsObject() ?? createNode;
    }
    public async Task<JsonArray> ListPlansAsync(string familyHandle)
    {
        var familiesResp = await _client.GetAsync("product_families.json");
        familiesResp.EnsureSuccessStatusCode();
        var familiesDoc = await familiesResp.Content.ReadAsStringAsync();
        var families = JsonNode.Parse(familiesDoc) as JsonArray ?? new JsonArray();
        int? familyId = null;
        foreach (var item in families)
        {
            var familyObj = item?.AsObject();
            if (familyObj != null && familyObj["handle"]?.GetValue<string>() == familyHandle)
            {
                familyId = familyObj["id"]?.GetValue<int>();
                break;
            }
        }
        if (familyId == null) return new JsonArray();
        var productsResp = await _client.GetAsync($"product_families/{familyId}/products.json");
        productsResp.EnsureSuccessStatusCode();
        var productsDoc = await productsResp.Content.ReadAsStringAsync();
        var products = JsonNode.Parse(productsDoc) as JsonArray ?? new JsonArray();
        return products;
    }
    public async Task<JsonObject> CreateSubscriptionAsync(string productHandle, string customerReference)
    {
        var payload = new JsonObject
        {
            ["subscription"] = new JsonObject
            {
                ["product_handle"] = productHandle,
                ["customer_reference"] = customerReference
            }
        };
        var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        var resp = await _client.PostAsync("subscriptions.json", content);
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(doc) as JsonObject;
        return node?["subscription"]?.AsObject() ?? node;
    }
    public async Task<JsonArray> ListCustomerSubscriptionsAsync(string customerReference)
    {
        var customer = await FindOrCreateCustomerAsync(customerReference, customerReference);
        if (customer == null || customer["id"] == null) return new JsonArray();
        var customerId = customer["id"]!.GetValue<int>();
        var resp = await _client.GetAsync($"customers/{customerId}/subscriptions.json");
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadAsStringAsync();
        var arr = JsonNode.Parse(doc) as JsonArray ?? new JsonArray();
        return arr;
    }
}
