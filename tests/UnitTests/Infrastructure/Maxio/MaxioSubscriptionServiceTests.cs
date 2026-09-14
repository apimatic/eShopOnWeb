using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Deterministic, network-free tests for the Maxio integration's idempotency logic,
/// driven through a fake HTTP handler.
/// </summary>
public class MaxioSubscriptionServiceTests
{
    private static readonly Subscriber TestSubscriber = new()
    {
        Reference = "demouser@microsoft.com",
        Email = "demouser@microsoft.com",
        FirstName = "Demo",
        LastName = "User"
    };

    private static (MaxioSubscriptionService Service, FakeHttpMessageHandler Handler) BuildService(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.chargify.com/") };
        var options = Options.Create(new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "test",
            ProductFamilyHandle = "eshop-subscribe"
        });
        var logger = Substitute.For<IAppLogger<MaxioSubscriptionService>>();
        return (new MaxioSubscriptionService(httpClient, options, logger), handler);
    }

    [Fact]
    public async Task SubscribeReturnsExistingSubscriptionWithoutCreatingDuplicate()
    {
        var (service, handler) = BuildService(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/customers/lookup.json")
            {
                return FakeHttpMessageHandler.Json(HttpStatusCode.OK, """{"customer":{"id":1,"reference":"demouser@microsoft.com"}}""");
            }

            if (request.Method == HttpMethod.Get && path == "/customers/1/subscriptions.json")
            {
                return FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    """[{"subscription":{"id":99,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}]""");
            }

            return FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "unexpected");
        });

        var result = await service.SubscribeAsync(TestSubscriber, "eshop-pro");

        Assert.Equal(99, result.Id);
        Assert.True(result.AlreadyExisted);
        // No new customer or subscription should have been created.
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/customers.json"));
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeCreatesCustomerAndSubscriptionWhenNoneExist()
    {
        var (service, handler) = BuildService(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/customers/lookup.json")
            {
                return FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, """{"errors":["not found"]}""");
            }

            if (request.Method == HttpMethod.Post && path == "/customers.json")
            {
                return FakeHttpMessageHandler.Json(HttpStatusCode.OK, """{"customer":{"id":2,"reference":"demouser@microsoft.com"}}""");
            }

            if (request.Method == HttpMethod.Get && path == "/customers/2/subscriptions.json")
            {
                return FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]");
            }

            if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
            {
                return FakeHttpMessageHandler.Json(HttpStatusCode.Created,
                    """{"subscription":{"id":100,"state":"active","payment_collection_method":"remittance","current_period_ends_at":"2026-08-29T16:58:42+05:00","product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}""");
            }

            return FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "unexpected");
        });

        var result = await service.SubscribeAsync(TestSubscriber, "eshop-pro");

        Assert.Equal(100, result.Id);
        Assert.False(result.AlreadyExisted);
        Assert.Equal("active", result.State);
        Assert.Equal("remittance", result.PaymentCollectionMethod);
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task GetSubscriptionsReturnsEmptyWhenNoCustomerExists()
    {
        var (service, handler) = BuildService(request =>
            FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, """{"errors":["not found"]}"""));

        var result = await service.GetSubscriptionsAsync(TestSubscriber);

        Assert.Empty(result);
        // Only the lookup should have been attempted.
        Assert.Equal(1, handler.CountOf(HttpMethod.Get, "/customers/lookup.json"));
    }
}
