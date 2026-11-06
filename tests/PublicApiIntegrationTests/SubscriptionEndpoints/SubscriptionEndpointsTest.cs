using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Runs against the real Maxio sandbox. The credentials come from the environment variables
/// MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN and MAXIO_ENVIRONMENT; the tests are inconclusive without them.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    private static WebApplicationFactory<Program>? _factory;

    [ClassInitialize]
    public static void Init(TestContext _)
    {
        var key = Environment.GetEnvironmentVariable("MAXIO_API_KEY");
        if (string.IsNullOrEmpty(key)) return;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Maxio:ApiKey"] = key,
                ["Maxio:Subdomain"] = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN"),
                ["Maxio:Environment"] = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT")
            })));
    }

    private static HttpClient Client(string token)
    {
        if (_factory is null) Assert.Inconclusive("Maxio sandbox credentials are not in the environment.");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [TestMethod]
    public async Task RequiresAuthentication()
    {
        if (_factory is null) Assert.Inconclusive("Maxio sandbox credentials are not in the environment.");
        var response = await _factory.CreateClient().GetAsync("/api/plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperCannotUseAdminEndpoints()
    {
        var response = await Client(ApiTokenHelper.GetNormalUserToken())
            .PostAsJsonAsync("/api/admin/plans", new { name = "x", price = 1, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscriptionLifecycle()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());

        var plans = await (await shopper.GetAsync("/api/plans")).Content.ReadFromJsonAsync<JsonElement>();
        var handles = new List<string>();
        foreach (var p in plans.GetProperty("plans").EnumerateArray()) handles.Add(p.GetProperty("handle").GetString()!);
        Assert.IsTrue(handles.Count >= 2, "the sandbox needs at least two subscribable plans");

        var created = await shopper.PostAsJsonAsync("/api/subscriptions", new { planHandle = handles[0] });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var sub = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = sub.GetProperty("id").GetInt64();

        try
        {
            var got = await shopper.GetFromJsonAsync<JsonElement>($"/api/subscriptions/{id}");
            Assert.AreEqual(handles[0], got.GetProperty("planHandle").GetString());
            Assert.IsTrue(got.TryGetProperty("paid", out _));

            Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"/api/subscriptions/{id}/pause", null)).StatusCode);
            Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"/api/subscriptions/{id}/pause", null)).StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"/api/subscriptions/{id}/resume", null)).StatusCode);

            Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"/api/subscriptions/{id}/cancel", null)).StatusCode);
            var switched = await shopper.PostAsJsonAsync($"/api/subscriptions/{id}/switch", new { planHandle = handles[1] });
            Assert.AreEqual(HttpStatusCode.OK, switched.StatusCode);
            var after = await switched.Content.ReadFromJsonAsync<JsonElement>();
            Assert.AreEqual(handles[1], after.GetProperty("planHandle").GetString());

            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            Assert.AreEqual(HttpStatusCode.OK, (await shopper.GetAsync($"/api/subscriptions/{id}/usage?date={today}")).StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync($"/api/subscriptions/{id}/usage?date=nope")).StatusCode);
        }
        finally
        {
            await shopper.PostAsync($"/api/subscriptions/{id}/cancel", null);
        }
    }

    [TestMethod]
    public async Task OtherUsersSubscriptionIsNotFound()
    {
        var response = await Client(ApiTokenHelper.GetNormalUserToken()).GetAsync("/api/subscriptions/1");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
