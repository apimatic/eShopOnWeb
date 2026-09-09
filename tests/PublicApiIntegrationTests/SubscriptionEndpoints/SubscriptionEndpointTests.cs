using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointTests
{
    private static HttpClient _client = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _client = ProgramTest.NewClient;
    }

    private static bool MaxioConfigured =>
        !string.IsNullOrEmpty(ProgramTest.Configuration["Maxio:ApiKey"]);

    private static async Task<HttpResponseMessage> PostAsync(string url, object payload, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> GetAsync(string url, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private static async Task<string> GetTokenAsync()
    {
        var response = await _client.PostAsync("api/authenticate",
            JsonContent.Create(new { username = "demouser@microsoft.com", password = "Pass@word1" }));
        var body = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode, $"authenticate failed: {(int)response.StatusCode} {body}");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("token").GetString()!;
    }

    [TestMethod]
    public async Task SubscriptionEndpointsRejectAnonymousRequests()
    {
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await GetAsync("api/subscription-plans", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await GetAsync("api/my-subscriptions", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await PostAsync("api/subscriptions", new { }, null)).StatusCode);
    }

    [TestMethod]
    public async Task SubscribeWithEmptyBodyReturnsBadRequest()
    {
        var token = await GetTokenAsync();
        var response = await PostAsync("api/subscriptions", new { }, token);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeFlowReturnsPlanPriceStateAndNextBillingDate()
    {
        if (!MaxioConfigured) { Assert.Inconclusive("Maxio credentials are not configured; skipping live Maxio test."); return; }

        var token = await GetTokenAsync();

        var plansResponse = await GetAsync("api/subscription-plans", token);
        Assert.AreEqual(HttpStatusCode.OK, plansResponse.StatusCode);
        var plansJson = await plansResponse.Content.ReadAsStringAsync();
        using (var plansDoc = JsonDocument.Parse(plansJson))
            Assert.IsTrue(plansDoc.RootElement.GetProperty("plans").GetArrayLength() > 0, "expected plans from the configured product family");

        // Subscribe (uses the first plan returned by the catalog).
        string handle;
        using (var plansDoc = JsonDocument.Parse(plansJson))
            handle = plansDoc.RootElement.GetProperty("plans")[0].GetProperty("handle").GetString()!;

        var first = await PostAsync("api/subscriptions", new { productHandle = handle }, token);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        var firstJson = await first.Content.ReadAsStringAsync();

        // Double-click: repeated subscribe must replay the same Maxio subscription.
        var second = await PostAsync("api/subscriptions", new { productHandle = handle }, token);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var secondJson = await second.Content.ReadAsStringAsync();
        Assert.AreEqual(Extract(firstJson, "maxioSubscriptionId"), Extract(secondJson, "maxioSubscriptionId"), "double subscribe must not create a second subscription");

        // The enrollment is reflected in the user's account.
        var mySubs = await GetAsync("api/my-subscriptions", token);
        Assert.AreEqual(HttpStatusCode.OK, mySubs.StatusCode);
        var mySubsJson = await mySubs.Content.ReadAsStringAsync();
        StringAssert.Contains(mySubsJson, Extract(firstJson, "maxioSubscriptionId"));
        Assert.AreEqual("active", Extract(mySubsJson, "state"));
        Assert.IsFalse(string.IsNullOrEmpty(Extract(mySubsJson, "nextBillingDate")));
    }

    private static string? Extract(string json, string property)
    {
        using var doc = JsonDocument.Parse(json);
        return FindProperty(doc.RootElement, property)?.ToString();
    }

    private static JsonElement? FindProperty(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in element.EnumerateObject())
            {
                if (string.Equals(p.Name, property, StringComparison.OrdinalIgnoreCase))
                    return p.Value;
                var nested = FindProperty(p.Value, property);
                if (nested is not null)
                    return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindProperty(item, property);
                if (nested is not null)
                    return nested;
            }
        }
        return null;
    }
}
