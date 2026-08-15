using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Exercises <see cref="MaxioBillingService"/> against a stubbed <see cref="HttpMessageHandler"/> —
/// the SDK client's constructor seam — so no network calls happen. Response bodies use the SDK wire
/// names/envelope shapes from the contract sheet.
/// </summary>
public class MaxioBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private const string PlanHandle = "eshop-pro";
    private const string UserRef = "demouser@microsoft.com";

    // A handler that replays queued responses in call order and records every request it received.
    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders;
        public List<HttpRequestMessage> Requests { get; } = new();

        public QueueHandler(IEnumerable<Func<HttpRequestMessage, HttpResponseMessage>> responders)
            => _responders = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responders);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var responder = _responders.Count > 0
                ? _responders.Dequeue()
                : (_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
            return Task.FromResult(responder(request));
        }
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Respond(HttpStatusCode status, string json) =>
        _ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static (MaxioBillingService Service, QueueHandler Handler) CreateService(
        params Func<HttpRequestMessage, HttpResponseMessage>[] responders)
    {
        var handler = new QueueHandler(responders);
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
        };
        options.Server.Production.Us.Site = "test";

        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test",
            ProductFamilyHandle = FamilyHandle,
        });
        var logger = Substitute.For<IAppLogger<MaxioBillingService>>();
        return (new MaxioBillingService(client, settings, logger), handler);
    }

    [Fact]
    public async Task ListPlansAsync_ResolvesFamilyByHandle_AndMapsPlans()
    {
        var (service, _) = CreateService(
            Respond(HttpStatusCode.OK,
                """[ { "product_family": { "id": 3023074, "handle": "eshop-subscribe", "name": "eShop Subscribe" } } ]"""),
            Respond(HttpStatusCode.OK,
                """[ { "product": { "id": 7126957, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } } ]"""));

        var plans = await service.ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(7126957, plan.ProductId);
        Assert.Equal(29900L, plan.PriceInCents);
        Assert.Equal(1, plan.Interval);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task ListPlansAsync_Throws_WhenConfiguredFamilyNotFound()
    {
        var (service, _) = CreateService(
            Respond(HttpStatusCode.OK,
                """[ { "product_family": { "id": 1, "handle": "some-other-family", "name": "Other" } } ]"""));

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.ListPlansAsync());
        Assert.Equal(404, ex.ProviderStatusCode);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        var (service, handler) = CreateService(
            // ensure customer: lookup miss
            Respond(HttpStatusCode.NotFound, """{ "errors": ["Customer not found"] }"""),
            // ensure customer: create
            Respond(HttpStatusCode.Created,
                $$"""{ "customer": { "id": 555, "reference": "{{UserRef}}", "email": "{{UserRef}}" } }"""),
            // dedupe: no existing subscriptions
            Respond(HttpStatusCode.OK, "[]"),
            // create subscription
            Respond(HttpStatusCode.Created,
                """{ "subscription": { "id": 999, "state": "active", "product_price_in_cents": 29900, "current_period_ends_at": "2026-09-16T00:00:00Z", "product": { "id": 7126957, "handle": "eshop-pro", "name": "Pro Plan" } } }"""));

        var result = await service.SubscribeAsync(new SubscribeRequest(UserRef, UserRef, PlanHandle));

        Assert.Equal(999, result.SubscriptionId);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal(29900L, result.PriceInCents);
        Assert.Equal(new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero), result.CurrentPeriodEndsAt);

        // Exactly one customer create and one subscription create.
        Assert.Equal(2, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingSubscription_WithoutCreating_WhenLiveOneExists()
    {
        var (service, handler) = CreateService(
            // ensure customer: found
            Respond(HttpStatusCode.OK,
                $$"""{ "customer": { "id": 555, "reference": "{{UserRef}}", "email": "{{UserRef}}" } }"""),
            // dedupe: an active subscription to the same plan already exists
            Respond(HttpStatusCode.OK,
                """[ { "subscription": { "id": 777, "state": "active", "product_price_in_cents": 29900, "product": { "id": 7126957, "handle": "eshop-pro", "name": "Pro Plan" } } } ]"""));

        var result = await service.SubscribeAsync(new SubscribeRequest(UserRef, UserRef, PlanHandle));

        Assert.Equal(777, result.SubscriptionId);
        // Idempotent: no create calls were made.
        Assert.Empty(handler.Requests.Where(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsClientError_OnValidationRejection()
    {
        var (service, _) = CreateService(
            Respond(HttpStatusCode.NotFound, """{ "errors": ["Customer not found"] }"""),
            Respond(HttpStatusCode.Created,
                $$"""{ "customer": { "id": 555, "reference": "{{UserRef}}", "email": "{{UserRef}}" } }"""),
            Respond(HttpStatusCode.OK, "[]"),
            // create subscription rejected
            Respond(HttpStatusCode.UnprocessableEntity, """{ "errors": ["Product handle: is invalid"] }"""));

        var ex = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(new SubscribeRequest(UserRef, UserRef, PlanHandle)));

        Assert.Equal(422, ex.ProviderStatusCode);
        Assert.True(ex.IsClientError);
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsClientError_WhenPlanHandleMissing()
    {
        var (service, _) = CreateService();

        var ex = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(new SubscribeRequest(UserRef, UserRef, string.Empty)));

        Assert.Equal(400, ex.ProviderStatusCode);
    }

    [Fact]
    public async Task ListSubscriptionsForUserAsync_ReturnsEmpty_WhenNoCustomer()
    {
        var (service, _) = CreateService(
            Respond(HttpStatusCode.NotFound, """{ "errors": ["Customer not found"] }"""));

        var result = await service.ListSubscriptionsForUserAsync(UserRef);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListSubscriptionsForUserAsync_MapsSubscriptions_WhenCustomerExists()
    {
        var (service, _) = CreateService(
            Respond(HttpStatusCode.OK,
                $$"""{ "customer": { "id": 555, "reference": "{{UserRef}}", "email": "{{UserRef}}" } }"""),
            Respond(HttpStatusCode.OK,
                """[ { "subscription": { "id": 321, "state": "active", "product_price_in_cents": 2900, "product": { "id": 7126958, "handle": "basic-plan", "name": "Basic Plan" } } } ]"""));

        var result = await service.ListSubscriptionsForUserAsync(UserRef);

        var sub = Assert.Single(result);
        Assert.Equal(321, sub.SubscriptionId);
        Assert.Equal("basic-plan", sub.PlanHandle);
        Assert.Equal("active", sub.State);
        Assert.Equal(2900L, sub.PriceInCents);
    }
}
