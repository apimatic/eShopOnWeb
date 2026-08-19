using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";

    [Fact]
    public async Task ListPlans_WhenFamilyIsMissing_ThrowsNotFound()
    {
        var service = CreateService(Json(HttpStatusCode.OK, "[]"));

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.ListPlansAsync(default));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task ListPlans_MapsCatalogProducts()
    {
        var families = """[{"product_family":{"id":3023074,"name":"eShop Subscribe","handle":"eshop-subscribe"}}]""";
        var products = """[{"product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro","description":"Pro monthly","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""";
        var service = CreateService(
            Json(HttpStatusCode.OK, families),
            Json(HttpStatusCode.OK, products));

        var plans = await service.ListPlansAsync(default);

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal(1, plan.Interval);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task ListSubscriptions_WhenCustomerIsMissing_ReturnsEmpty()
    {
        var service = CreateService(Json(HttpStatusCode.NotFound, "{}"));

        var result = await service.ListSubscriptionsForUserAsync("demouser@microsoft.com", default);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Subscribe_WhenPlanIsUnknown_ThrowsNotFound()
    {
        var service = CreateService(Json(HttpStatusCode.NotFound, "{}"));

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(
            new SubscribeToPlan("user-1", "user-1@example.com", "User", "One", "missing-plan"),
            default));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Subscribe_WhenAlreadyEnrolled_ReturnsExistingWithoutCreate()
    {
        var product = """{"product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro","description":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}""";
        var customer = """{"customer":{"id":42,"email":"user-1@example.com","first_name":"User","last_name":"One","reference":"user-1"}}""";
        var subscriptions = """[{"subscription":{"id":99,"state":"active","current_period_ends_at":"2026-09-19T00:00:00Z","product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}]""";
        var handler = new StubHandler(
            Json(HttpStatusCode.OK, product),
            Json(HttpStatusCode.OK, customer),
            Json(HttpStatusCode.OK, subscriptions));
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(
            new SubscribeToPlan("user-1", "user-1@example.com", "User", "One", "eshop-pro"),
            default);

        Assert.False(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        Assert.Equal("eshop-pro", result.Subscription.ProductHandle);
        Assert.Equal("active", result.Subscription.State);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    private static MaxioBillingService CreateService(params HttpResponseMessage[] responses) =>
        CreateService(new StubHandler(responses));

    private static MaxioBillingService CreateService(StubHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.test") },
            new MaxioAdvancedBillingClientOptions
            {
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(2), MaxRetries = 1 }
            });

        return new MaxioBillingService(
            client,
            Options.Create(new MaxioOptions
            {
                ApiKey = "test-key",
                Subdomain = "example",
                ProductFamilyHandle = FamilyHandle
            }),
            Substitute.For<IAppLogger<MaxioBillingService>>());
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (_responses.Count == 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
