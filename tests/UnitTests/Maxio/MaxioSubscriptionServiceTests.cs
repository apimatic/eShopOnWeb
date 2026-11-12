using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string PlansJson =
        """[{"product":{"handle":"eshop-pro","name":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}},{"product":{"handle":"basic-plan","name":"Basic","price_in_cents":2900,"interval":1,"interval_unit":"month"}}]""";
    private const string CustomerJson = """{"customer":{"id":11,"reference":"u1","email":"u1@test.local"}}""";
    private const string SubscriptionJson =
        """{"subscription":{"id":22,"state":"active","next_assessment_at":"2026-12-01T00:00:00Z","product_price_in_cents":29900,"reference":"u1:eshop-pro","product":{"handle":"eshop-pro","name":"Pro"}}}""";

    private sealed class FakeMaxio : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path)> Calls { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage?>? Override { get; set; }
        public bool SubscriptionExists { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Calls.Add((request.Method, path));
            var response = Override?.Invoke(request) ?? Default(request, path);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        private HttpResponseMessage Default(HttpRequestMessage r, string path)
        {
            if (path.EndsWith("/products.json")) return Json(PlansJson);
            if (path == "/customers.json") return Json(CustomerJson, HttpStatusCode.Created);
            if (path == "/customers/lookup.json") return Json(CustomerJson);
            if (path == "/subscriptions.json") { SubscriptionExists = true; return Json(SubscriptionJson, HttpStatusCode.Created); }
            if (path == "/subscriptions/lookup.json")
                return SubscriptionExists ? Json(SubscriptionJson) : new HttpResponseMessage(HttpStatusCode.NotFound);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        public int Writes(string path) => Calls.Count(c => c.Method == HttpMethod.Post && c.Path == path);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static MaxioSubscriptionService NewService(FakeMaxio fake, string dbName)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
            Retry = RetryOptions.Disabled()
        };
        options.Server.Production.Us.BaseUrl = "https://maxio.stub.test";
        var client = new MaxioAdvancedBillingClient(new HttpClient(fake), options);
        var db = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(dbName).Options);
        var settings = Options.Create(new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "fam", BaseUrl = "https://x.test" });
        return new MaxioSubscriptionService(client, db, settings, NullLogger<MaxioSubscriptionService>.Instance);
    }

    [Fact]
    public async Task Subscribe_twice_creates_one_customer_and_one_subscription()
    {
        var fake = new FakeMaxio();
        var db = Guid.NewGuid().ToString();

        var first = await NewService(fake, db).SubscribeAsync("u1", "u1@test.local", "eshop-pro", default);
        var second = await NewService(fake, db).SubscribeAsync("u1", "u1@test.local", "eshop-pro", default);

        Assert.False(first.AlreadySubscribed);
        Assert.True(second.AlreadySubscribed);
        Assert.Equal(22, second.Subscription.SubscriptionId);
        Assert.Equal(1, fake.Writes("/customers.json"));
        Assert.Equal(1, fake.Writes("/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_returns_plan_price_state_and_next_billing_date()
    {
        var result = await NewService(new FakeMaxio(), Guid.NewGuid().ToString())
            .SubscribeAsync("u1", "u1@test.local", "eshop-pro", default);

        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), result.Subscription.NextBillingDate);
    }

    [Fact]
    public async Task Unknown_plan_is_rejected_before_any_write()
    {
        var fake = new FakeMaxio();

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(() =>
            NewService(fake, Guid.NewGuid().ToString()).SubscribeAsync("u1", "u1@test.local", "not-offered", default));

        Assert.Equal(MaxioBillingFailure.UnknownPlan, ex.Failure);
        Assert.Equal(0, fake.Writes("/customers.json"));
        Assert.Equal(0, fake.Writes("/subscriptions.json"));
    }

    [Fact]
    public async Task Customer_write_connection_failure_is_settled_by_reading_back_by_reference()
    {
        var fake = new FakeMaxio();
        fake.Override = r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/customers.json"
            ? throw new HttpRequestException("connection reset")
            : null; // the customer did land: the lookup by reference finds it

        var result = await NewService(fake, Guid.NewGuid().ToString())
            .SubscribeAsync("u1", "u1@test.local", "eshop-pro", default);

        Assert.Equal(22, result.Subscription.SubscriptionId);
        Assert.Equal(1, fake.Writes("/customers.json"));            // never resent
        Assert.Contains(fake.Calls, c => c.Path == "/customers/lookup.json");
    }

    [Fact]
    public async Task Subscription_write_connection_failure_is_settled_by_reading_back_by_reference()
    {
        var fake = new FakeMaxio();
        fake.Override = r =>
        {
            if (r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/subscriptions.json")
            {
                fake.SubscriptionExists = true; // the write landed, but the response was lost
                throw new HttpRequestException("connection reset");
            }
            return null;
        };

        var result = await NewService(fake, Guid.NewGuid().ToString())
            .SubscribeAsync("u1", "u1@test.local", "eshop-pro", default);

        Assert.Equal(22, result.Subscription.SubscriptionId);
        Assert.Equal(1, fake.Writes("/subscriptions.json"));
        Assert.Contains(fake.Calls, c => c.Path == "/subscriptions/lookup.json");
    }

    [Fact]
    public async Task Provider_rejection_is_reported_and_releases_the_claim_so_a_retry_can_proceed()
    {
        var fake = new FakeMaxio();
        var db = Guid.NewGuid().ToString();
        fake.Override = r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/subscriptions.json"
            ? Json("""{"errors":["No payment method was on file"]}""", HttpStatusCode.UnprocessableEntity)
            : null;

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(() =>
            NewService(fake, db).SubscribeAsync("u1", "u1@test.local", "eshop-pro", default));
        Assert.Equal(MaxioBillingFailure.Rejected, ex.Failure);

        fake.Override = null;
        var retry = await NewService(fake, db).SubscribeAsync("u1", "u1@test.local", "eshop-pro", default);
        Assert.False(retry.AlreadySubscribed);
    }

    [Fact]
    public void Settings_fail_fast_on_missing_values_and_accept_a_base_url_instead_of_a_subdomain()
    {
        Assert.NotNull(MaxioSettings.Validate(new MaxioSettings { ProductFamilyHandle = "f", Subdomain = "s" }));            // no key
        Assert.NotNull(MaxioSettings.Validate(new MaxioSettings { ApiKey = " ", ProductFamilyHandle = "f", Subdomain = "s" }));
        Assert.NotNull(MaxioSettings.Validate(new MaxioSettings { ApiKey = "k", Subdomain = "s" }));                        // no family
        Assert.NotNull(MaxioSettings.Validate(new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f" }));              // no subdomain/base url
        Assert.NotNull(MaxioSettings.Validate(new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f", BaseUrl = "nope" }));
        Assert.Null(MaxioSettings.Validate(new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f", BaseUrl = "https://h.test" }));
        Assert.Null(MaxioSettings.Validate(new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f", Subdomain = "s" }));
    }
}
