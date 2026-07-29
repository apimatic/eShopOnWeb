using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioBillingServiceTests
{
    private static readonly BillingCustomerInfo Customer =
        new(reference: "user-1", email: "user-1@example.com", firstName: "User", lastName: "One");

    private const string ProductsJson =
        "[{\"product\":{\"id\":2,\"name\":\"Pro Plan\",\"handle\":\"eshop-pro\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":false,\"taxable\":false}}," +
        "{\"product\":{\"id\":1,\"name\":\"Basic Plan\",\"handle\":\"basic-plan\",\"price_in_cents\":2900,\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":false,\"taxable\":false}}]";

    private static MaxioSettings ConfiguredSettings() => new()
    {
        ApiKey = "key",
        Subdomain = "test-site",
        ProductFamilyHandle = "fam-under-test"
    };

    private static MaxioBillingService BuildService(MaxioSettings settings, StubHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://test-site.chargify.com/") };
        var client = new MaxioApiClient(http, NullLogger<MaxioApiClient>.Instance);
        return new MaxioBillingService(client, settings, NullLogger<MaxioBillingService>.Instance);
    }

    [Fact]
    public async Task GetPlans_MapsProductsAndOrdersByPrice()
    {
        var handler = new StubHandler()
            .OnGet("/product_families/handle:fam-under-test/products.json", ProductsJson);
        var service = BuildService(ConfiguredSettings(), handler);

        var plans = (await service.GetPlansAsync()).ToList();

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic-plan", plans[0].Handle); // cheapest first
        Assert.Equal("eshop-pro", plans[1].Handle);
        Assert.Equal(29900, plans[1].PriceInCents);
        Assert.Equal("299.00", plans[1].FormattedPrice);
        Assert.False(plans[1].RequiresPaymentMethod);
    }

    [Fact]
    public async Task Subscribe_WhenNotConfigured_Throws()
    {
        var service = BuildService(new MaxioSettings(), new StubHandler());

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(Customer, "eshop-pro"));

        Assert.Equal(SubscriptionBillingError.NotConfigured, ex.Error);
    }

    [Fact]
    public async Task Subscribe_WhenPlanUnknown_ThrowsNotFound()
    {
        var handler = new StubHandler()
            .OnGet("/product_families/handle:fam-under-test/products.json", ProductsJson);
        var service = BuildService(ConfiguredSettings(), handler);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(Customer, "no-such-plan"));

        Assert.Equal(SubscriptionBillingError.NotFound, ex.Error);
    }

    [Fact]
    public async Task Subscribe_WhenLiveSubscriptionExists_ReturnsItWithoutCreating()
    {
        const string customerJson = "{\"customer\":{\"id\":100,\"reference\":\"user-1\",\"email\":\"user-1@example.com\"}}";
        const string subsJson =
            "[{\"subscription\":{\"id\":500,\"state\":\"active\",\"product_price_in_cents\":29900," +
            "\"current_period_ends_at\":\"2026-08-29T00:00:00Z\",\"created_at\":\"2026-07-29T00:00:00Z\"," +
            "\"product\":{\"handle\":\"eshop-pro\",\"name\":\"Pro Plan\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\"}," +
            "\"customer\":{\"id\":100}}}]";

        var handler = new StubHandler()
            .OnGet("/product_families/handle:fam-under-test/products.json", ProductsJson)
            .OnGet("/customers/lookup.json", customerJson)
            .OnGet("/customers/100/subscriptions.json", subsJson);
        var service = BuildService(ConfiguredSettings(), handler);

        var result = await service.SubscribeAsync(Customer, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(500, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(0, handler.PostCount); // idempotent: no create was issued
    }

    [Fact]
    public async Task Subscribe_WhenPlanRequiresPaymentMethod_ThrowsValidation()
    {
        const string cardRequiredProducts =
            "[{\"product\":{\"id\":9,\"name\":\"Card Plan\",\"handle\":\"card-plan\",\"price_in_cents\":1000,\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":true,\"taxable\":false}}]";
        var handler = new StubHandler()
            .OnGet("/product_families/handle:fam-under-test/products.json", cardRequiredProducts);
        var service = BuildService(ConfiguredSettings(), handler);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(Customer, "card-plan"));

        Assert.Equal(SubscriptionBillingError.Validation, ex.Error);
        Assert.Equal(0, handler.PostCount);
    }

    /// <summary>Routes canned responses by HTTP method + request path.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _getRoutes = new();
        public int PostCount { get; private set; }

        public StubHandler OnGet(string path, string json)
        {
            _getRoutes[path] = json;
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                PostCount++;
            }

            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && _getRoutes.TryGetValue(path, out var json))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"errors\":[\"not stubbed\"]}", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
