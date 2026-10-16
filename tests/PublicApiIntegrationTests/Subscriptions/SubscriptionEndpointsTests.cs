using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Subscriptions;

[TestClass]
public class SubscriptionEndpointsTests
{
    private static async Task<HttpClient> Shopper(BillingTestHost host, string username = "demouser@microsoft.com")
    {
        var client = host.CreateClient();
        var response = await client.PostAsJsonAsync("/api/authenticate", new { username, password = "Pass@word1" });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("token").GetString());
        return client;
    }

    [TestMethod]
    public async Task AllSubscriptionEndpointsRequireBearerAuthentication()
    {
        using var host = new BillingTestHost();
        using var client = host.CreateClient();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "plan-a" })).StatusCode);
        Assert.AreEqual(0, host.Provider.Requests.Count);
    }

    [TestMethod]
    public async Task BrowseSubscribeRepeatAndAccountUseProviderStateAndConfiguredBaseUrl()
    {
        using var host = new BillingTestHost();
        using var client = await Shopper(host);
        var catalog = await client.GetFromJsonAsync<SubscriptionPlans>("/api/subscription-plans");
        Assert.AreEqual(2, catalog!.Plans.Count);
        Assert.IsFalse(catalog.IsTruncated);
        var created = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "plan-a" });
        Assert.AreEqual(HttpStatusCode.OK, created.StatusCode);
        var result = (await created.Content.ReadFromJsonAsync<SubscribeResult>())!;
        Assert.AreEqual("active", result.Subscription!.State);
        Assert.AreEqual(1700L, result.Subscription.PriceInCents);
        Assert.AreEqual(DateTimeOffset.Parse("2027-01-01T00:00:00Z"), result.Subscription.NextBillingDate);
        var repeated = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "plan-a" });
        Assert.AreEqual(result.Subscription.Id, (await repeated.Content.ReadFromJsonAsync<SubscribeResult>())!.Subscription!.Id);
        var mine = await client.GetFromJsonAsync<MySubscriptions>("/api/my-subscriptions");
        Assert.AreEqual(1, mine!.Subscriptions.Count);
        Assert.IsFalse(mine.HasPendingRequests);
        Assert.AreEqual(1, host.Provider.CustomerWrites);
        Assert.AreEqual(1, host.Provider.SubscriptionWrites);
        Assert.IsTrue(host.Provider.Requests.All(r => r.Url.StartsWith("https://billing.invalid/custom-base/", StringComparison.Ordinal)));
        var write = host.Provider.Requests.Single(r => r.Method == "POST" && r.Url.EndsWith("/subscriptions.json"));
        using var body = JsonDocument.Parse(write.Body!);
        var payload = body.RootElement.GetProperty("subscription");
        Assert.AreEqual("remittance", payload.GetProperty("payment_collection_method").GetString());
        Assert.IsFalse(payload.TryGetProperty("product_id", out _));
        Assert.IsFalse(payload.TryGetProperty("next_billing_at", out _));
        Assert.IsFalse(payload.TryGetProperty("credit_card_attributes", out _));
    }

    [TestMethod]
    public async Task SimultaneousSubscribesAcrossRequestsSendOneCustomerAndOneSubscriptionWrite()
    {
        using var host = new BillingTestHost();
        using var client = await Shopper(host);
        var responses = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "plan-a" })));
        Assert.IsTrue(responses.All(r => r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted));
        var retry = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "plan-a" });
        Assert.AreEqual(HttpStatusCode.OK, retry.StatusCode);
        Assert.AreEqual(1, host.Provider.CustomerWrites);
        Assert.AreEqual(1, host.Provider.SubscriptionWrites);
        Assert.AreEqual(1, (await client.GetFromJsonAsync<MySubscriptions>("/api/my-subscriptions"))!.Subscriptions.Count);
    }

    [TestMethod]
    public async Task DifferentPlansShareCustomerAndDifferentUsersCannotReadEachOther()
    {
        using var host = new BillingTestHost();
        using var client = await Shopper(host);
        using var other = await Shopper(host, "admin@microsoft.com");
        (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "plan-a", customerId = 999999, userId = "ignored" })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "plan-b" })).EnsureSuccessStatusCode();
        Assert.AreEqual(1, host.Provider.CustomerWrites);
        Assert.AreEqual(2, host.Provider.SubscriptionWrites);
        Assert.AreEqual(0, (await other.GetFromJsonAsync<MySubscriptions>("/api/my-subscriptions"))!.Subscriptions.Count);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("outside-family")]
    public async Task InvalidPlanCannotCreateBillingEntities(string handle)
    {
        using var host = new BillingTestHost();
        using var client = await Shopper(host);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = handle })).StatusCode);
        Assert.AreEqual(0, host.Provider.CustomerWrites);
        Assert.AreEqual(0, host.Provider.SubscriptionWrites);
    }

    [TestMethod]
    public async Task LostCustomerResponseIsReconciledWithoutASecondWrite()
    {
        using var host = new BillingTestHost();
        host.Provider.DropCustomerResponse = true;
        using var client = await Shopper(host);
        var result = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "plan-a" });
        Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
        Assert.AreEqual(1, host.Provider.CustomerWrites);
        Assert.AreEqual(1, host.Provider.SubscriptionWrites);
    }

    [TestMethod]
    public async Task UnknownCustomerWriteNeverReplaysAndSweepCanRecoverIt()
    {
        using var host = new BillingTestHost();
        host.Provider.DropCustomerBeforeCommit = true;
        using var client = await Shopper(host);
        Assert.AreEqual(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "plan-a" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "plan-b" })).StatusCode);
        Assert.AreEqual(1, host.Provider.CustomerWrites);
        Assert.AreEqual(0, host.Provider.SubscriptionWrites);
        var sent = host.Provider.Requests.Single(r => r.Method == "POST" && r.Url.EndsWith("/customers.json"));
        using var body = JsonDocument.Parse(sent.Body!);
        var reference = body.RootElement.GetProperty("customer").GetProperty("reference").GetString()!;
        host.Provider.Customers[reference] = JsonSerializer.SerializeToElement(new { id = 500, reference });
        using (var scope = host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SubscriptionBillingService>().ReconcilePendingAsync(CancellationToken.None);
        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "plan-a" })).StatusCode);
        Assert.AreEqual(1, host.Provider.CustomerWrites);
        Assert.AreEqual(1, host.Provider.SubscriptionWrites);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LostOrMalformedSubscriptionResponseIsReconciledWithoutASecondWrite(bool malformed)
    {
        using var host = new BillingTestHost();
        host.Provider.DropSubscriptionResponse = !malformed;
        host.Provider.MalformedSubscriptionSuccess = malformed;
        using var client = await Shopper(host);
        var result = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "plan-a" });
        Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
        Assert.IsNotNull((await result.Content.ReadFromJsonAsync<SubscribeResult>())!.Subscription);
        Assert.AreEqual(1, host.Provider.SubscriptionWrites);
    }

    [TestMethod]
    public async Task UnresolvedWriteRemainsPendingAndSweepSettlesItWithoutReplay()
    {
        using var host = new BillingTestHost();
        host.Provider.DropSubscriptionBeforeCommit = true;
        using var client = await Shopper(host);
        var first = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "plan-a" });
        Assert.AreEqual(HttpStatusCode.Accepted, first.StatusCode);
        Assert.AreEqual("/api/my-subscriptions", first.Headers.Location!.OriginalString);
        var again = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "plan-a" });
        Assert.AreEqual(HttpStatusCode.Accepted, again.StatusCode);
        Assert.IsTrue((await client.GetFromJsonAsync<MySubscriptions>("/api/my-subscriptions"))!.HasPendingRequests);
        Assert.AreEqual(1, host.Provider.SubscriptionWrites);
        var sent = host.Provider.Requests.Single(r => r.Method == "POST" && r.Url.EndsWith("/subscriptions.json"));
        using var body = JsonDocument.Parse(sent.Body!);
        var payload = body.RootElement.GetProperty("subscription");
        var reference = payload.GetProperty("reference").GetString()!;
        var customer = host.Provider.Customers.Values.Single();
        host.Provider.Subscriptions[reference] = JsonSerializer.SerializeToElement(new
        {
            id = 700, reference, customer,
            product = new { handle = "plan-a", product_family = new { handle = "test-family" } }, state = "active"
        });
        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SubscriptionBillingService>().ReconcilePendingAsync(CancellationToken.None);
            Assert.IsFalse(await scope.ServiceProvider.GetRequiredService<CatalogContext>().BillingClaims.AnyAsync(x => x.RemoteId == null));
        }
        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "plan-a" })).StatusCode);
        Assert.AreEqual(1, host.Provider.SubscriptionWrites);
    }

    [TestMethod]
    public async Task DefinitiveValidationRejectionReleasesClaimForCorrectedRetry()
    {
        using var host = new BillingTestHost();
        host.Provider.RejectSubscription = true;
        using var client = await Shopper(host);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "plan-a" })).StatusCode);
        host.Provider.RejectSubscription = false;
        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "plan-a" })).StatusCode);
        Assert.AreEqual(1, host.Provider.CustomerWrites);
        Assert.AreEqual(2, host.Provider.SubscriptionWrites);
    }

    [TestMethod]
    [DataRow(401, 502)]
    [DataRow(429, 503)]
    [DataRow(422, 422)]
    public async Task ProviderFailuresAreDistinctAndSanitized(int providerStatus, int expected)
    {
        using var host = new BillingTestHost();
        host.Provider.CatalogError = providerStatus;
        using var client = await Shopper(host);
        var response = await client.GetAsync("/api/subscription-plans");
        Assert.AreEqual(expected, (int)response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(text.Contains("provider detail", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MaxioAdvancedBilling", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("billing.invalid", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task MalformedCatalogFailsExplicitlyAndCannotTriggerCreate()
    {
        using var host = new BillingTestHost();
        host.Provider.MalformedCatalog = true;
        using var client = await Shopper(host);
        Assert.AreEqual(HttpStatusCode.BadGateway, (await client.GetAsync("/api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadGateway, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "plan-a" })).StatusCode);
        Assert.AreEqual(0, host.Provider.CustomerWrites);
    }

    [TestMethod]
    public async Task CatalogCapIsVisibleAndSubscriptionFailsClosed()
    {
        using var host = new BillingTestHost();
        host.Provider.FullCatalogPages = true;
        using var client = await Shopper(host);
        Assert.IsTrue((await client.GetFromJsonAsync<SubscriptionPlans>("/api/subscription-plans"))!.IsTruncated);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "plan-a" })).StatusCode);
        Assert.AreEqual(0, host.Provider.CustomerWrites);
    }

    [TestMethod]
    public void MissingConfigurationFailsFast()
    {
        foreach (var settings in new[]
        {
            new MaxioSettings { Subdomain = "site", ProductFamilyHandle = "family" },
            new MaxioSettings { ApiKey = "placeholder", ProductFamilyHandle = "family" },
            new MaxioSettings { ApiKey = "placeholder", Subdomain = "site" },
            new MaxioSettings { ApiKey = "placeholder", Subdomain = "site", ProductFamilyHandle = "family", BaseUrl = " " }
        }) Assert.ThrowsException<InvalidOperationException>(settings.Validate);
    }
}
