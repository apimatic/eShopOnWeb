using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.eShopWeb.PublicApi.IntegrationTests;

/// <summary>
/// Integration tests for Maxio subscription endpoints.
/// Tests verify endpoint discovery, authentication, and basic response structure.
/// Note: Full functionality requires Maxio sandbox credentials.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTests
{
    private static WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        _factory = new WebApplicationFactory<Program>();
    }

    [TestInitialize]
    public void TestInitialize()
    {
        _client = _factory?.CreateClient();
    }

    [TestMethod]
    public async Task GetSubscriptionPlans_EndpointIsDiscoverable()
    {
        // Verify the GET /api/subscription-plans endpoint exists and responds
        var response = await _client!.GetAsync("/api/subscription-plans");
        Assert.AreNotEqual(HttpStatusCode.NotFound, response.StatusCode,
            "Endpoint should exist (not 404)");
    }

    [TestMethod]
    public async Task CreateSubscription_EndpointIsDiscoverable()
    {
        // Verify the POST /api/subscriptions endpoint exists
        var response = await _client!.PostAsync(
            "/api/subscriptions",
            new StringContent(
                JsonSerializer.Serialize(new { productHandle = "test" }),
                Encoding.UTF8,
                "application/json"));
        Assert.AreNotEqual(HttpStatusCode.NotFound, response.StatusCode,
            "Endpoint should exist (not 404)");
    }

    [TestMethod]
    public async Task GetMySubscriptions_EndpointIsDiscoverable()
    {
        // Verify the GET /api/my-subscriptions endpoint exists
        var response = await _client!.GetAsync("/api/my-subscriptions");
        Assert.AreNotEqual(HttpStatusCode.NotFound, response.StatusCode,
            "Endpoint should exist (not 404)");
    }

    [TestMethod]
    public async Task GetMySubscriptions_WithoutAuth_ReturnsUnauthorized()
    {
        // Verify protected endpoint requires authentication
        var response = await _client!.GetAsync("/api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode,
            "Protected endpoint should return 401 Unauthorized");
    }

    [TestMethod]
    public async Task CreateSubscription_WithoutAuth_ReturnsUnauthorized()
    {
        // Verify protected endpoint requires authentication
        var response = await _client!.PostAsync(
            "/api/subscriptions",
            new StringContent(
                JsonSerializer.Serialize(new { productHandle = "test-plan" }),
                Encoding.UTF8,
                "application/json"));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode,
            "Protected endpoint should return 401 Unauthorized");
    }

    [TestMethod]
    public async Task GetSubscriptionPlans_ReturnsValidJsonResponse()
    {
        // Verify the response contains valid JSON structure
        var response = await _client!.GetAsync("/api/subscription-plans");
        var content = await response.Content.ReadAsStringAsync();

        Assert.IsNotNull(content, "Response should contain content");
        Assert.IsTrue(content.Length > 0, "Response should not be empty");

        // Verify it's valid JSON
        try
        {
            var json = JsonDocument.Parse(content);
            Assert.IsTrue(json.RootElement.ValueKind == JsonValueKind.Object,
                "Response should be a JSON object");
        }
        catch (JsonException)
        {
            Assert.Fail("Response should be valid JSON");
        }
    }
}
