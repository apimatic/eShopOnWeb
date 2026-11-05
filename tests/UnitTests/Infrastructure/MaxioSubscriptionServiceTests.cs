using System.Net;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure;

public class MaxioSubscriptionServiceTests
{
    private const string FamilyHandle = "test-family";
    private const string Products =
        """[{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","product_family":{"handle":"test-family"}}}]""";
    private const string Customer = """{"customer":{"id":10,"reference":"r"}}""";
    private const string Subscription =
        """{"subscription":{"id":20,"state":"active","product_price_in_cents":29900,"next_assessment_at":"2026-12-01T00:00:00Z","product":{"handle":"eshop-pro","name":"Pro Plan","product_family":{"handle":"test-family"}}}}""";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<string> Calls { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static MaxioSubscriptionService CreateService(StubHandler handler, string dbName)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
            Retry = RetryOptions.Disabled(),
        });
        var db = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(dbName).Options);
        var userManager = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        var settings = Options.Create(new MaxioSettings { ApiKey = "test-key", Subdomain = "x", ProductFamilyHandle = FamilyHandle });
        return new MaxioSubscriptionService(client, db, userManager, settings, NullLogger<MaxioSubscriptionService>.Instance);
    }

    private static HttpResponseMessage Happy(HttpRequestMessage r) => r.RequestUri!.AbsolutePath switch
    {
        var p when p.EndsWith("/products.json") => Json(HttpStatusCode.OK, Products),
        "/customers.json" => Json(HttpStatusCode.Created, Customer),
        "/subscriptions.json" when r.Method == HttpMethod.Post => Json(HttpStatusCode.Created, Subscription),
        "/subscriptions/lookup.json" => Json(HttpStatusCode.OK, Subscription),
        _ => Json(HttpStatusCode.NotFound, "{}"),
    };

    [Fact]
    public async Task SubscribingTwiceCreatesOneCustomerAndOneSubscription()
    {
        var handler = new StubHandler(Happy);
        var db = Guid.NewGuid().ToString();

        var first = await CreateService(handler, db).SubscribeAsync("user@test.com", "eshop-pro", default);
        var second = await CreateService(handler, db).SubscribeAsync("user@test.com", "eshop-pro", default);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(20, second.Subscription.SubscriptionId);
        Assert.Equal(1, handler.Calls.Count(c => c == "POST /customers.json"));
        Assert.Equal(1, handler.Calls.Count(c => c == "POST /subscriptions.json"));
    }

    [Fact]
    public async Task UnknownPlanIsRejectedBeforeAnythingIsClaimedOrWritten()
    {
        var handler = new StubHandler(Happy);

        var ex = await Assert.ThrowsAsync<SubscriptionException>(
            () => CreateService(handler, Guid.NewGuid().ToString()).SubscribeAsync("user@test.com", "not-offered", default));

        Assert.Equal(SubscriptionFailure.InvalidPlan, ex.Failure);
        Assert.DoesNotContain(handler.Calls, c => c.StartsWith("POST"));
    }

    [Fact]
    public async Task ConnectionFailureOnCreateIsSettledByReferenceLookup()
    {
        var handler = new StubHandler(r =>
            r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/subscriptions.json"
                ? throw new HttpRequestException("connection reset")
                : Happy(r));

        var result = await CreateService(handler, Guid.NewGuid().ToString()).SubscribeAsync("user@test.com", "eshop-pro", default);

        Assert.Equal(20, result.Subscription.SubscriptionId);
        Assert.Contains("GET /subscriptions/lookup.json", handler.Calls);
        Assert.Equal(1, handler.Calls.Count(c => c == "POST /subscriptions.json"));
    }

    [Fact]
    public async Task ConnectionFailureWithNothingFoundReleasesTheClaimSoARetrySucceeds()
    {
        var failing = true;
        var handler = new StubHandler(r =>
        {
            if (failing && r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/subscriptions.json")
                throw new HttpRequestException("connection reset");
            if (failing && r.RequestUri!.AbsolutePath == "/subscriptions/lookup.json")
                return Json(HttpStatusCode.NotFound, "{}");
            return Happy(r);
        });
        var db = Guid.NewGuid().ToString();

        var ex = await Assert.ThrowsAsync<SubscriptionException>(
            () => CreateService(handler, db).SubscribeAsync("user@test.com", "eshop-pro", default));
        Assert.Equal(SubscriptionFailure.Unavailable, ex.Failure);

        failing = false;
        var retry = await CreateService(handler, db).SubscribeAsync("user@test.com", "eshop-pro", default);
        Assert.True(retry.Created);
    }

    [Fact]
    public async Task MySubscriptionsIsEmptyForAUserWithoutACustomer()
    {
        var handler = new StubHandler(Happy);

        var list = await CreateService(handler, Guid.NewGuid().ToString()).GetSubscriptionsAsync("nobody@test.com", default);

        Assert.Empty(list);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public void SettingsValidatorNamesEveryMissingKeyButNoValues()
    {
        var result = new MaxioSettingsValidator().Validate(null, new MaxioSettings { ApiKey = " ", BaseUrl = "not a url" });

        Assert.True(result.Failed);
        Assert.Contains("Maxio:ApiKey", result.FailureMessage);
        Assert.Contains("Maxio:Subdomain", result.FailureMessage);
        Assert.Contains("Maxio:ProductFamilyHandle", result.FailureMessage);
        Assert.Contains("Maxio:BaseUrl", result.FailureMessage);
    }
}
