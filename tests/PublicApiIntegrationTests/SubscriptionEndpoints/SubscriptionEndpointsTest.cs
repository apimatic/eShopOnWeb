using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    // Matches Maxio:ProductFamilyHandle in appsettings.test.json.
    private const string ProductsPath = "/product_families/handle:test-family/products.json";

    private static (HttpClient Client, StubMaxio Maxio) CreateClient(string? token)
    {
        var maxio = new StubMaxio();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(MaxioBillingServiceCollectionExtensions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => maxio)));
        var client = factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return (client, maxio);
    }

    [DataTestMethod]
    [DataRow("GET", "api/subscription-plans")]
    [DataRow("GET", "api/my-subscriptions")]
    [DataRow("POST", "api/subscriptions")]
    public async Task ReturnsUnauthorizedWithoutToken(string method, string path)
    {
        var (client, maxio) = CreateClient(token: null);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = method == "POST" ? Json(new { planHandle = "eshop-pro" }) : null
        });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual(0, maxio.Requests.Count);
    }

    [TestMethod]
    public async Task ListsPlansFromMaxio()
    {
        var (client, maxio) = CreateClient(ApiTokenHelper.GetNormalUserToken());
        maxio.Routes[$"GET {ProductsPath}"] = (HttpStatusCode.OK,
            """[{"product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""");

        var response = await client.GetAsync("api/subscription-plans");

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var plan = json.RootElement.GetProperty("plans")[0];
        Assert.AreEqual("eshop-pro", plan.GetProperty("handle").GetString());
        Assert.AreEqual(299m, plan.GetProperty("price").GetDecimal());
        Assert.IsFalse(json.RootElement.GetProperty("truncated").GetBoolean());
    }

    [TestMethod]
    public async Task RejectsMissingPlanHandleWithoutCallingMaxio()
    {
        var (client, maxio) = CreateClient(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", Json(new { }));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, maxio.Requests.Count);
    }

    [TestMethod]
    public async Task RejectsPlanOutsideTheConfiguredFamily()
    {
        var (client, maxio) = CreateClient(ApiTokenHelper.GetNormalUserToken());
        maxio.Routes[$"GET {ProductsPath}"] = (HttpStatusCode.OK, """[{"product":{"handle":"eshop-pro","name":"Pro Plan"}}]""");

        var response = await client.PostAsync("api/subscriptions", Json(new { planHandle = "someone-elses-plan" }));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.IsFalse(maxio.Requests.Contains("POST /subscriptions.json"));
    }

    [TestMethod]
    public async Task ProviderOutageIsA503WithoutLeakingProviderDetails()
    {
        var (client, _) = CreateClient(ApiTokenHelper.GetNormalUserToken());
        // No route for the products page: the stub answers 500.

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.DoesNotMatch(body, new System.Text.RegularExpressions.Regex("maxio\\.test|MaxioAdvancedBilling|ApiException"));
    }

    private static StringContent Json(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    /// <summary>Stands in for the Maxio HTTP API behind the SDK's named HttpClient.</summary>
    private sealed class StubMaxio : HttpMessageHandler
    {
        public ConcurrentDictionary<string, (HttpStatusCode Status, string Json)> Routes { get; } = new();

        public ConcurrentBag<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = $"{request.Method} {Uri.UnescapeDataString(request.RequestUri!.AbsolutePath)}";
            Requests.Add(key);
            var (status, json) = Routes.TryGetValue(key, out var route) ? route : (HttpStatusCode.InternalServerError, "");
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
                RequestMessage = request
            });
        }
    }
}
