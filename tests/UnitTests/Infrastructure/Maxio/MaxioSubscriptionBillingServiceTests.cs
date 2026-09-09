using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private const string ProductFamilyJson =
        "[{\"product\":{\"id\":7126958,\"name\":\"Basic Plan\",\"handle\":\"basic-plan\",\"price_in_cents\":2900," +
        "\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":false," +
        "\"product_family\":{\"id\":3023074,\"handle\":\"eshop-subscribe\",\"name\":\"eShop\"}}}," +
        "{\"product\":{\"id\":7126957,\"name\":\"Pro Plan\",\"handle\":\"eshop-pro\",\"price_in_cents\":29900," +
        "\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":true," +
        "\"product_family\":{\"id\":3023074,\"handle\":\"eshop-subscribe\",\"name\":\"eShop\"}}}," +
        "{\"product\":{\"id\":1,\"name\":\"Archived Plan\",\"handle\":\"archived\",\"price_in_cents\":100," +
        "\"interval\":1,\"interval_unit\":\"month\",\"archived_at\":\"2026-01-01T00:00:00Z\"," +
        "\"product_family\":{\"id\":3023074,\"handle\":\"eshop-subscribe\",\"name\":\"eShop\"}}}]";

    private const string CustomerJson =
        "{\"customer\":{\"id\":55,\"reference\":\"eshopweb-user:user-1\",\"email\":\"shopper@example.com\"," +
        "\"first_name\":\"shopper\",\"last_name\":\"eShop Customer\"}}";

    private const string CreatedSubscriptionJson =
        "{\"subscription\":{\"id\":9001,\"state\":\"active\",\"customer_id\":55," +
        "\"product\":{\"id\":7126957,\"name\":\"Pro Plan\",\"handle\":\"eshop-pro\"}," +
        "\"product_price_in_cents\":29900,\"current_period_ends_at\":\"2026-10-09T00:00:00Z\"," +
        "\"activated_at\":\"2026-09-09T00:00:00Z\",\"created_at\":\"2026-09-09T00:00:00Z\"," +
        "\"updated_at\":\"2026-09-09T00:00:00Z\"}}";

    private const string NoSubscriptionsJson = "[]";

    private readonly IAppLoggerStub _logger = new IAppLoggerStub();

    [Fact]
    public async Task ListPlansMapsProductsAndSkipsArchived()
    {
        var handler = new StubMaxioHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/products.json"))
            {
                return Json(HttpStatusCode.OK, ProductFamilyJson);
            }

            return Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);

        var plans = await service.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        var basic = plans.Single(p => p.Handle == "basic-plan");
        Assert.Equal(2900, basic.PriceInCents);
        Assert.Equal("month", basic.IntervalUnit);
        Assert.False(basic.RequiresPaymentMethod);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.True(pro.RequiresPaymentMethod);
    }

    [Fact]
    public async Task SubscribeReusesExistingCustomerInsteadOfCreatingAnother()
    {
        var postCustomers = 0;
        var handler = new StubMaxioHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/products.json", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, ProductFamilyJson);
            }

            if (req.RequestUri.AbsolutePath == "/customers/lookup.json")
            {
                return Json(HttpStatusCode.OK, CustomerJson);
            }

            if (req.RequestUri.AbsolutePath == "/customers.json")
            {
                Interlocked.Increment(ref postCustomers);
                return Json(HttpStatusCode.OK, CustomerJson);
            }

            if (req.RequestUri.AbsolutePath.EndsWith("/subscriptions.json", StringComparison.Ordinal) &&
                req.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, NoSubscriptionsJson);
            }

            if (req.RequestUri.AbsolutePath == "/subscriptions.json")
            {
                return Json(HttpStatusCode.Created, CreatedSubscriptionJson);
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method} {req.RequestUri}");
        });

        var service = CreateService(handler);

        var result = await service.SubscribeAsync("user-1", "shopper", "shopper@example.com", "eshop-pro");

        Assert.True(result.CreatedNew);
        Assert.Equal(0, postCustomers);
        Assert.Equal(9001, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.Equal(new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc), result.Subscription.NextBillingDateUtc);
    }

    [Fact]
    public async Task SubscribeCreatesCustomerWhenLookupMisses()
    {
        var postCustomers = 0;
        var handler = new StubMaxioHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/products.json", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, ProductFamilyJson);
            }

            if (req.RequestUri.AbsolutePath == "/customers/lookup.json")
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }

            if (req.RequestUri.AbsolutePath == "/customers.json")
            {
                Interlocked.Increment(ref postCustomers);
                return Json(HttpStatusCode.Created, CustomerJson);
            }

            if (req.RequestUri.AbsolutePath.EndsWith("/subscriptions.json", StringComparison.Ordinal) &&
                req.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, NoSubscriptionsJson);
            }

            if (req.RequestUri.AbsolutePath == "/subscriptions.json")
            {
                return Json(HttpStatusCode.Created, CreatedSubscriptionJson);
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method} {req.RequestUri}");
        });

        var service = CreateService(handler);

        await service.SubscribeAsync("user-1", "shopper", "shopper@example.com", "eshop-pro");

        Assert.Equal(1, postCustomers);
        var createCustomerRequest = handler.RequestBodies.Single(r =>
            r.Path == "/customers.json");
        Assert.Contains("\"reference\":\"eshopweb-user:user-1\"", createCustomerRequest.Body);
    }

    [Fact]
    public async Task SubscribeIsIdempotentWhenLiveSubscriptionAlreadyExists()
    {
        var postSubscriptions = 0;
        var existingSubscriptionJson =
            "[{\"subscription\":{\"id\":9001,\"state\":\"active\",\"customer_id\":55," +
            "\"product\":{\"id\":7126957,\"name\":\"Pro Plan\",\"handle\":\"eshop-pro\"}," +
            "\"product_price_in_cents\":29900,\"current_period_ends_at\":\"2026-10-09T00:00:00Z\"," +
            "\"created_at\":\"2026-09-09T00:00:00Z\",\"updated_at\":\"2026-09-09T00:00:00Z\"}}]";

        var handler = new StubMaxioHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/products.json", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, ProductFamilyJson);
            }

            if (req.RequestUri.AbsolutePath == "/customers/lookup.json")
            {
                return Json(HttpStatusCode.OK, CustomerJson);
            }

            if (req.RequestUri.AbsolutePath == "/customers/55/subscriptions.json" &&
                req.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, existingSubscriptionJson);
            }

            if (req.RequestUri.AbsolutePath == "/subscriptions.json")
            {
                Interlocked.Increment(ref postSubscriptions);
                return Json(HttpStatusCode.Created, CreatedSubscriptionJson);
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method} {req.RequestUri}");
        });

        var service = CreateService(handler);

        var first = await service.SubscribeAsync("user-1", "shopper", "shopper@example.com", "eshop-pro");
        var second = await service.SubscribeAsync("user-1", "shopper", "shopper@example.com", "eshop-pro");

        Assert.False(first.CreatedNew);
        Assert.False(second.CreatedNew);
        Assert.Equal(9001, second.Subscription.Id);
        Assert.Equal(0, postSubscriptions);
    }

    [Fact]
    public async Task SubscribeWithUnknownPlanThrowsPlanNotFound()
    {
        var handler = new StubMaxioHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/products.json", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, ProductFamilyJson);
            }

            if (req.RequestUri.AbsolutePath == "/customers/lookup.json")
            {
                return Json(HttpStatusCode.OK, CustomerJson);
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method} {req.RequestUri}");
        });

        var service = CreateService(handler);

        await Assert.ThrowsAsync<PlanNotFoundException>(() =>
            service.SubscribeAsync("user-1", "shopper", "shopper@example.com", "does-not-exist"));
    }

    [Fact]
    public async Task ListUserSubscriptionsReturnsMappedDetails()
    {
        var existingSubscriptionJson =
            "[{\"subscription\":{\"id\":9001,\"state\":\"active\",\"customer_id\":55," +
            "\"product\":{\"id\":7126957,\"name\":\"Pro Plan\",\"handle\":\"eshop-pro\"}," +
            "\"product_price_in_cents\":29900,\"current_period_ends_at\":\"2026-10-09T00:00:00Z\"," +
            "\"created_at\":\"2026-09-09T00:00:00Z\",\"updated_at\":\"2026-09-09T00:00:00Z\"}}]";

        var handler = new StubMaxioHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/customers/lookup.json")
            {
                return Json(HttpStatusCode.OK, CustomerJson);
            }

            if (req.RequestUri.AbsolutePath == "/customers/55/subscriptions.json")
            {
                return Json(HttpStatusCode.OK, existingSubscriptionJson);
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method} {req.RequestUri}");
        });

        var service = CreateService(handler);

        var subscriptions = await service.ListUserSubscriptionsAsync("user-1", "shopper", "shopper@example.com");

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(9001, subscription.Id);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("Pro Plan", subscription.PlanName);
        Assert.Equal(29900, subscription.PriceInCents);
    }

    [Fact]
    public void BuildCustomerReferenceProducesDeterministicValue()
    {
        Assert.Equal("eshopweb-user:abc-123", MaxioSubscriptionBillingService.BuildCustomerReference("abc-123"));
        Assert.Equal("eshopweb-user:abc-123", MaxioSubscriptionBillingService.BuildCustomerReference(" abc-123 "));
    }

    [Fact]
    public void ClientWithoutConfigurationThrowsWithKeyNames()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new MaxioApiClient(Options.Create(new MaxioOptions())));

        Assert.Contains("Maxio:ApiKey", exception.Message);
        Assert.Contains("Maxio:ProductFamilyHandle", exception.Message);
    }

    private MaxioSubscriptionBillingService CreateService(StubMaxioHandler handler)
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-api-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe",
            BaseUrl = "https://unit.test"
        });

        var client = new MaxioApiClient(options, handler);
        var logger = Substitute.For<Microsoft.eShopWeb.ApplicationCore.Interfaces.IAppLogger<MaxioSubscriptionBillingService>>();
        return new MaxioSubscriptionBillingService(client, options, logger);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class IAppLoggerStub
    {
    }

    private sealed class StubMaxioHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubMaxioHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<(string Path, string? Body)> RequestBodies { get; } = new List<(string, string?)>();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await (request.Content.ReadAsStringAsync(cancellationToken));
            RequestBodies.Add((request.RequestUri!.AbsolutePath, body));
            return _responder(request);
        }
    }
}
