using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services;

public class MaxioSubscriptionBillingServiceTests
{
    private const string Username = "demouser@microsoft.com";
    private const string Email = "demouser@microsoft.com";
    private const string ProductHandle = "eshop-pro";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders;

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string?> RequestBodies { get; } = new();

        public StubHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responders)
        {
            _responders = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responders);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            // The SDK disposes the request after sending, so capture the body now.
            RequestBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            if (_responders.Count == 0)
            {
                throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
            }
            return _responders.Dequeue()(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private static (MaxioSubscriptionBillingService Service, StubHandler Handler) CreateService(
        params Func<HttpRequestMessage, HttpResponseMessage>[] responders)
    {
        var handler = new StubHandler(responders);
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        var settings = Options.Create(new MaxioSettings { ProductFamilyHandle = "eshop-subscribe" });
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Substitute.For<IAppLogger<MaxioSubscriptionBillingService>>();
        return (new MaxioSubscriptionBillingService(client, settings, cache, logger), handler);
    }

    [Fact]
    public async Task ListPlans_ReturnsMappedPlans()
    {
        var (service, handler) = CreateService(
            _ => Json(HttpStatusCode.OK, """[{"product_family":{"id":3023074,"handle":"eshop-subscribe","name":"eShop Subscribe"}}]"""),
            _ => Json(HttpStatusCode.OK, """[{"product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]"""));

        var plans = await service.ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal(7126957, plan.Id);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal(1, plan.Interval);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("/product_families", handler.Requests[0].RequestUri!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListPlans_ThrowsBillingException_WhenFamilyNotFound()
    {
        var (service, _) = CreateService(
            _ => Json(HttpStatusCode.OK, """[{"product_family":{"id":1,"handle":"other-family","name":"Other"}}]"""));

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.ListPlansAsync());

        Assert.Equal((int)HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        var (service, handler) = CreateService(
            _ => Json(HttpStatusCode.NotFound, ""),                                                              // FindSubscription
            _ => Json(HttpStatusCode.NotFound, ""),                                                              // ReadCustomerByReference
            _ => Json(HttpStatusCode.Created, """{"customer":{"id":123,"reference":"demouser@microsoft.com"}}"""), // CreateCustomer
            _ => Json(HttpStatusCode.Created, """{"subscription":{"id":555,"state":"active","product_price_in_cents":29900,"next_assessment_at":"2026-09-26T00:00:00Z","product":{"name":"Pro Plan","handle":"eshop-pro"}}}"""));

        var result = await service.SubscribeAsync(Username, Email, ProductHandle);

        Assert.Equal(555, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal("Pro Plan", result.PlanName);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal(29900, result.PriceInCents);
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero), result.NextBillingDate);

        var createRequest = handler.Requests[^1];
        Assert.Equal(HttpMethod.Post, createRequest.Method);
        var sentJson = handler.RequestBodies[^1]!;
        Assert.Contains("\"product_handle\":\"eshop-pro\"", sentJson);
        Assert.Contains("\"customer_id\":123", sentJson);
        Assert.Contains("\"reference\":\"demouser@microsoft.com:eshop-pro\"", sentJson);
    }

    [Fact]
    public async Task Subscribe_ReturnsExistingSubscription_WithoutCreating_WhenAlreadySubscribed()
    {
        var (service, handler) = CreateService(
            _ => Json(HttpStatusCode.OK, """{"subscription":{"id":555,"state":"active","product_price_in_cents":29900,"next_assessment_at":"2026-09-26T00:00:00Z","product":{"name":"Pro Plan","handle":"eshop-pro"}}}"""));

        var result = await service.SubscribeAsync(Username, Email, ProductHandle);

        Assert.Equal(555, result.Id);
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }

    [Fact]
    public async Task Subscribe_ThrowsBillingException422_WithProviderMessages_WhenRejected()
    {
        var (service, _) = CreateService(
            _ => Json(HttpStatusCode.NotFound, ""),                                                              // FindSubscription
            _ => Json(HttpStatusCode.OK, """{"customer":{"id":123,"reference":"demouser@microsoft.com"}}"""),     // ReadCustomerByReference
            _ => Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Product: must be present"]}"""),        // CreateSubscription
            _ => Json(HttpStatusCode.NotFound, ""));                                                             // reconcile FindSubscription

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(Username, Email, ProductHandle));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("Product: must be present", ex.Message);
    }

    [Fact]
    public async Task ListSubscriptions_ReturnsEmpty_WhenUserHasNoCustomer()
    {
        var (service, _) = CreateService(
            _ => Json(HttpStatusCode.NotFound, ""));

        var result = await service.ListSubscriptionsAsync(Username);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListSubscriptions_MapsStatePriceAndNextBillingDate()
    {
        var (service, _) = CreateService(
            _ => Json(HttpStatusCode.OK, """{"customer":{"id":123,"reference":"demouser@microsoft.com"}}"""),
            _ => Json(HttpStatusCode.OK, """[{"subscription":{"id":555,"state":"active","product_price_in_cents":29900,"current_period_ends_at":"2026-09-26T00:00:00Z","product":{"name":"Pro Plan","handle":"eshop-pro"}}}]"""));

        var result = await service.ListSubscriptionsAsync(Username);

        var subscription = Assert.Single(result);
        Assert.Equal(555, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal(29900, subscription.PriceInCents);
        // NextAssessmentAt is null in the payload, so CurrentPeriodEndsAt is the fallback.
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero), subscription.NextBillingDate);
    }
}
