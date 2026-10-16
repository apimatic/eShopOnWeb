using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Microsoft.eShopWeb.FunctionalTests.Billing;

public class SubscriptionEndpointTests
{
    private sealed class Factory(BillingSandbox sandbox, string environment = "Testing") : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseOnlyInMemoryDatabase"] = "true", ["Maxio:ApiKey"] = "stub-key", ["Maxio:Subdomain"] = "stub-site",
                ["Maxio:ProductFamilyHandle"] = "family", ["Maxio:BaseUrl"] = "http://billing.invalid/proxy"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<MaxioAdvancedBilling.MaxioAdvancedBillingClient>();
                services.AddSingleton(sandbox.Client());
            });
        }
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task InvalidJsonAndForgedIdentityMembersReturn400InBothHostEnvironments(string environment)
    {
        using var sandbox = new BillingSandbox();
        using var factory = new Factory(sandbox, environment);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await Login(client, "demouser@microsoft.com");
        var forged = await client.PostAsJsonAsync("/api/subscriptions", new
        {
            ProductHandle = "pro", FirstName = "A", LastName = "B", CustomerId = 9999, UserId = "forged-account"
        });
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        var invalidJson = await client.PostAsync("/api/subscriptions", new StringContent("{invalid-json", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidJson.StatusCode);
        foreach (var response in new[] { forged, invalidJson })
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("JsonException", body);
            Assert.DoesNotContain("SubscribeRequest", body);
            Assert.DoesNotContain("forged-account", body);
        }
        Assert.Empty(sandbox.Requests);
    }

    [Fact]
    public async Task AllRoutesRequireJwt()
    {
        using var sandbox = new BillingSandbox();
        using var factory = new Factory(sandbox);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/subscription-plans")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/my-subscriptions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/subscriptions", new { ProductHandle = "pro", FirstName = "A", LastName = "B" })).StatusCode);
        Assert.Empty(sandbox.Requests);
    }

    private static async Task Login(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/authenticate", new { Username = username, Password = AuthorizationConstants.DEFAULT_PASSWORD });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("result").GetBoolean());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
    }

    [Fact]
    public async Task AuthenticatedEnrollmentReplayAccountIsolationAndBodyValidation()
    {
        using var sandbox = new BillingSandbox();
        using var factory = new Factory(sandbox);
        using var shopper = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var other = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await Login(shopper, "demouser@microsoft.com");
        await Login(other, "admin@microsoft.com");
        Assert.Equal(HttpStatusCode.OK, (await shopper.GetAsync("/api/subscription-plans")).StatusCode);
        var payload = new { ProductHandle = "pro", FirstName = "Ada", LastName = "Shopper" };
        var created = await shopper.PostAsJsonAsync("/api/subscriptions", payload);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var first = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("active", first.GetProperty("subscription").GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.OK, (await shopper.PostAsJsonAsync("/api/subscriptions", payload)).StatusCode);
        var own = await (await shopper.GetAsync("/api/my-subscriptions")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(own.GetProperty("subscriptions").EnumerateArray());
        var none = await (await other.GetAsync("/api/my-subscriptions")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(none.GetProperty("subscriptions").EnumerateArray());
        Assert.Equal(HttpStatusCode.Created, (await other.PostAsJsonAsync("/api/subscriptions", payload)).StatusCode);
        var customerPosts = sandbox.Requests.Where(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.EndsWith("/customers.json")).ToArray();
        Assert.Equal(2, customerPosts.Length);
        Assert.NotEqual(customerPosts[0].Body, customerPosts[1].Body);
        var forged = await shopper.PostAsJsonAsync("/api/subscriptions", new { ProductHandle = "basic", FirstName = "A", LastName = "B", CustomerId = 9999, UserId = "other" });
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await shopper.PostAsJsonAsync("/api/subscriptions", new { ProductHandle = "", FirstName = "A", LastName = "B" })).StatusCode);
        Assert.Equal(4, sandbox.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task ReadDriftHasSafePublicErrorAndUnknownWriteReturns202()
    {
        using var sandbox = new BillingSandbox { Fault = "subscription-drop", PublishSubscription = false };
        using var factory = new Factory(sandbox);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await Login(client, "demouser@microsoft.com");
        var response = await client.PostAsJsonAsync("/api/subscriptions", new { ProductHandle = "pro", FirstName = "A", LastName = "B" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        sandbox.Reads = _ => BillingSandbox.Raw("{\"customer\":\"malformed sensitive\"}");
        var failed = await client.GetAsync("/api/my-subscriptions");
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        var body = await failed.Content.ReadAsStringAsync();
        Assert.DoesNotContain("MaxioAdvancedBilling", body);
        Assert.DoesNotContain("billing.invalid", body);
        Assert.DoesNotContain("sensitive", body);
    }
}
