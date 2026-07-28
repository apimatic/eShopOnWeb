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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Tests <see cref="MaxioBillingService"/> against the real Maxio SDK client, faking only the
/// transport (an <see cref="HttpMessageHandler"/> that returns canned wire JSON). This exercises the
/// actual model mapping, the idempotency guard, and the SDK error-to-domain translation without any
/// network calls. The handler returns the scripted responses in the exact order the service issues
/// its calls.
/// </summary>
public class MaxioBillingServiceTests
{
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Json)> _responses;

        public List<HttpRequestMessage> Requests { get; } = new();

        public ScriptedHandler(params (HttpStatusCode Status, string Json)[] responses)
            => _responses = new Queue<(HttpStatusCode, string)>(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var (status, json) = _responses.Count > 0 ? _responses.Dequeue() : (HttpStatusCode.OK, "{}");
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }

        public int PostCount => Requests.Count(r => r.Method == HttpMethod.Post);
    }

    private static MaxioBillingService CreateService(ScriptedHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(
            new HttpClient(handler),
            new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" }
            });

        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test",
            ProductFamilyHandle = "eshop-subscribe"
        });

        var logger = Substitute.For<IAppLogger<MaxioBillingService>>();
        return new MaxioBillingService(client, settings, logger);
    }

    private static SubscribeRequest Request(string plan = "eshop-pro") => new()
    {
        CustomerReference = "user@example.com",
        Email = "user@example.com",
        FirstName = "user",
        LastName = "example",
        PlanHandle = plan
    };

    [Fact]
    public async Task GetPlansAsync_ResolvesFamilyByHandle_AndMapsProducts()
    {
        var handler = new ScriptedHandler(
            (HttpStatusCode.OK, """[{"product_family":{"id":42,"handle":"eshop-subscribe","name":"eShop"}}]"""),
            (HttpStatusCode.OK, """[{"product":{"id":7,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","product_family":{"handle":"eshop-subscribe"}}}]"""));

        var plans = await CreateService(handler).GetPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(7, plan.ProductId);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal(299m, plan.Price);
        Assert.Equal(1, plan.Interval);
        Assert.Equal("month", plan.IntervalUnit);
        // Maxio does not expose currency on the product model.
        Assert.Null(plan.Currency);
    }

    [Fact]
    public async Task SubscribeAsync_WhenNotYetSubscribed_CreatesSubscriptionAndMapsConfirmation()
    {
        var handler = new ScriptedHandler(
            (HttpStatusCode.OK, """{"customer":{"id":555,"reference":"user@example.com"}}"""),
            (HttpStatusCode.OK, "[]"),
            (HttpStatusCode.Created, """{"subscription":{"id":123,"state":"active","currency":"USD","product_price_in_cents":29900,"current_period_ends_at":"2026-08-28T00:00:00+00:00","product":{"handle":"eshop-pro","name":"Pro Plan"}}}"""));

        var result = await CreateService(handler).SubscribeAsync(Request());

        Assert.Equal(123, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal(29900, result.PriceInCents);
        Assert.Equal("USD", result.Currency);
        Assert.Equal(new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero), result.NextBillingAt);
        // One POST: the subscription create (customer already existed).
        Assert.Equal(1, handler.PostCount);
    }

    [Fact]
    public async Task SubscribeAsync_WhenAlreadySubscribedToPlan_ReusesWithoutCreating()
    {
        var handler = new ScriptedHandler(
            (HttpStatusCode.OK, """{"customer":{"id":555,"reference":"user@example.com"}}"""),
            (HttpStatusCode.OK, """[{"subscription":{"id":999,"state":"active","currency":"USD","product_price_in_cents":29900,"current_period_ends_at":"2026-08-28T00:00:00+00:00","product":{"handle":"eshop-pro","name":"Pro Plan"}}}]"""));

        var result = await CreateService(handler).SubscribeAsync(Request());

        // Reuses the existing subscription — the double-submit guard.
        Assert.Equal(999, result.Id);
        Assert.Equal("active", result.State);
        // Crucially, no POST was issued: no duplicate customer, no duplicate subscription.
        Assert.Equal(0, handler.PostCount);
    }

    [Fact]
    public async Task SubscribeAsync_WhenCustomerMissing_CreatesCustomerThenSubscribes()
    {
        var handler = new ScriptedHandler(
            (HttpStatusCode.NotFound, "{}"),
            (HttpStatusCode.Created, """{"customer":{"id":777,"reference":"user@example.com"}}"""),
            (HttpStatusCode.OK, "[]"),
            (HttpStatusCode.Created, """{"subscription":{"id":321,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan"}}}"""));

        var result = await CreateService(handler).SubscribeAsync(Request());

        Assert.Equal(321, result.Id);
        // Two POSTs: create customer, then create subscription.
        Assert.Equal(2, handler.PostCount);
    }

    [Fact]
    public async Task SubscribeAsync_WhenMaxioRejectsSubscription_ThrowsValidationBillingException()
    {
        var handler = new ScriptedHandler(
            (HttpStatusCode.OK, """{"customer":{"id":555,"reference":"user@example.com"}}"""),
            (HttpStatusCode.OK, "[]"),
            (HttpStatusCode.UnprocessableEntity, """{"errors":["Product with API Handle 'bad' does not exist for this site."]}"""));

        var ex = await Assert.ThrowsAsync<BillingException>(
            () => CreateService(handler).SubscribeAsync(Request("bad")));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public async Task SubscribeAsync_WithBlankPlanHandle_ThrowsValidationBillingException()
    {
        var handler = new ScriptedHandler();

        var ex = await Assert.ThrowsAsync<BillingException>(
            () => CreateService(handler).SubscribeAsync(Request(plan: "  ")));

        Assert.Equal(400, ex.StatusCode);
        // Rejected before any network call.
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetSubscriptionsForCustomerAsync_WhenCustomerNotFound_ReturnsEmpty()
    {
        var handler = new ScriptedHandler((HttpStatusCode.NotFound, """{"errors":["not found"]}"""));

        var result = await CreateService(handler).GetSubscriptionsForCustomerAsync("missing@example.com");

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSubscriptionsForCustomerAsync_MapsEachSubscription()
    {
        var handler = new ScriptedHandler(
            (HttpStatusCode.OK, """{"customer":{"id":555,"reference":"user@example.com"}}"""),
            (HttpStatusCode.OK, """[{"subscription":{"id":1,"state":"active","currency":"USD","product_price_in_cents":29900,"product":{"handle":"eshop-pro","name":"Pro Plan"}}},{"subscription":{"id":2,"state":"active","currency":"USD","product_price_in_cents":2900,"product":{"handle":"basic-plan","name":"Basic Plan"}}}]"""));

        var result = await CreateService(handler).GetSubscriptionsForCustomerAsync("user@example.com");

        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.PlanHandle == "eshop-pro" && s.PriceInCents == 29900);
        Assert.Contains(result, s => s.PlanHandle == "basic-plan" && s.PriceInCents == 2900);
    }
}
