using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Xunit;

namespace Microsoft.eShopWeb.FunctionalTests.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Endpoint-handler tests. The seam faked here is <see cref="IMaxioBillingService"/> — no calls reach
/// Maxio — so these verify the endpoints' identity handling, mapping, and response shaping.
/// </summary>
public class SubscriptionEndpointsTests
{
    [Fact]
    public async Task PlansEndpoint_ReturnsMappedPlansWithFormattedPrice()
    {
        var billing = new FakeMaxioBillingService
        {
            Plans = new List<SubscriptionPlan>
            {
                new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Currency = "USD", Interval = 1, IntervalUnit = "month" },
                new() { Handle = "tri", Name = "Tri", PriceInCents = 9000, Currency = "USD", Interval = 3, IntervalUnit = "month" }
            }
        };
        var endpoint = new SubscriptionPlansListEndpoint();

        var result = await endpoint.HandleAsync(billing, CancellationToken.None);

        var ok = Assert.IsType<Ok<ListSubscriptionPlansResponse>>(result);
        Assert.Equal(2, ok.Value!.Plans.Count);
        Assert.Equal("$299.00/month", ok.Value.Plans[0].FormattedPrice);
        // Multi-period interval pluralizes.
        Assert.Equal("$90.00/3 months", ok.Value.Plans[1].FormattedPrice);
    }

    [Fact]
    public async Task CreateSubscription_WithoutIdentity_ReturnsUnauthorized()
    {
        var billing = new FakeMaxioBillingService();
        var endpoint = new CreateSubscriptionEndpoint();

        var result = await endpoint.HandleAsync(
            new CreateSubscriptionRequest { Username = "", PlanHandle = "eshop-pro" }, billing, CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        Assert.Null(billing.LastSubscribeRequest);
    }

    [Fact]
    public async Task CreateSubscription_ThreadsIdentityAndReturnsMappedResult()
    {
        var billing = new FakeMaxioBillingService
        {
            SubscribeResult = new SubscribeResult(
                new CustomerSubscription
                {
                    SubscriptionId = 555,
                    State = "active",
                    PlanHandle = "eshop-pro",
                    PlanName = "Pro Plan",
                    PriceInCents = 29900,
                    Currency = "USD"
                },
                alreadyExisted: true)
        };
        var endpoint = new CreateSubscriptionEndpoint();

        var result = await endpoint.HandleAsync(
            new CreateSubscriptionRequest { Username = "shopper@example.com", PlanHandle = "eshop-pro" }, billing, CancellationToken.None);

        // The authenticated identity is used as both the customer reference and email.
        Assert.Equal("shopper@example.com", billing.LastSubscribeRequest!.UserReference);
        Assert.Equal("shopper@example.com", billing.LastSubscribeRequest.Email);
        Assert.Equal("eshop-pro", billing.LastSubscribeRequest.PlanHandle);

        var ok = Assert.IsType<Ok<CreateSubscriptionResponse>>(result);
        Assert.True(ok.Value!.AlreadyExisted);
        Assert.Equal(555, ok.Value.Subscription!.SubscriptionId);
        Assert.Equal("active", ok.Value.Subscription.State);
    }

    [Fact]
    public async Task MySubscriptions_ReturnsMappedSubscriptionsForCaller()
    {
        var billing = new FakeMaxioBillingService
        {
            SubscriptionsByReference =
            {
                ["shopper@example.com"] = new List<CustomerSubscription>
                {
                    new() { SubscriptionId = 1, State = "active", PlanHandle = "eshop-pro", PlanName = "Pro Plan", PriceInCents = 29900, Currency = "USD" }
                }
            }
        };
        var endpoint = new MySubscriptionsListEndpoint();

        var result = await endpoint.HandleAsync("shopper@example.com", billing, CancellationToken.None);

        var ok = Assert.IsType<Ok<ListMySubscriptionsResponse>>(result);
        Assert.Single(ok.Value!.Subscriptions);
        Assert.Equal("eshop-pro", ok.Value.Subscriptions[0].PlanHandle);
        Assert.Equal("$299.00", ok.Value.Subscriptions[0].FormattedPrice);
    }

    private sealed class FakeMaxioBillingService : IMaxioBillingService
    {
        public List<SubscriptionPlan> Plans { get; set; } = new();
        public SubscribeResult? SubscribeResult { get; set; }
        public SubscribeRequest? LastSubscribeRequest { get; private set; }
        public Dictionary<string, List<CustomerSubscription>> SubscriptionsByReference { get; } = new();

        public Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlansAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SubscriptionPlan>>(Plans);

        public Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
        {
            LastSubscribeRequest = request;
            return Task.FromResult(SubscribeResult
                ?? new SubscribeResult(new CustomerSubscription { PlanHandle = request.PlanHandle ?? "" }, false));
        }

        public Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default)
        {
            var list = SubscriptionsByReference.TryGetValue(userReference, out var subs)
                ? subs
                : new List<CustomerSubscription>();
            return Task.FromResult<IReadOnlyList<CustomerSubscription>>(list);
        }
    }
}
