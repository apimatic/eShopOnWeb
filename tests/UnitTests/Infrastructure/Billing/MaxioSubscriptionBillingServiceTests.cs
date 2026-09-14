using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;
using static Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.MaxioTestHarness;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceGetPlans
{
    private static readonly SubscriberIdentity Subscriber = new(SubscriberEmail);

    [Fact]
    public async Task MapsPlanFieldsFromTheProvider()
    {
        var stub = new MaxioStubHandler().WithFamily().WithSeededPlans();

        var plans = await ServiceOver(stub).GetPlansAsync();

        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal("Everything, monthly.", pro.Description);
        Assert.Equal(29900L, pro.PriceInCents);
        Assert.Equal(299m, pro.Price);
        Assert.Equal("USD", pro.Currency);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
    }

    [Fact]
    public async Task ExcludesArchivedPlans()
    {
        var stub = new MaxioStubHandler().WithFamily().WithSeededPlans();

        var plans = await ServiceOver(stub).GetPlansAsync();

        Assert.DoesNotContain(plans, p => p.Handle == "retired-plan");
        Assert.Equal(2, plans.Count);
    }

    [Fact]
    public async Task ReportsRequiresPaymentMethodFromRequireCreditCardOnly()
    {
        // request_credit_card is deprecated and true on plans that subscribe fine without a card;
        // treating it as a requirement would tell shoppers a card is needed when it is not.
        var stub = new MaxioStubHandler().WithFamily().WithSeededPlans();

        var plans = await ServiceOver(stub).GetPlansAsync();

        Assert.False(plans.Single(p => p.Handle == "eshop-pro").RequiresPaymentMethod);
        Assert.True(plans.Single(p => p.Handle == "basic-plan").RequiresPaymentMethod);
    }

    [Fact]
    public async Task ResolvesTheFamilyByHandleNotByAConfiguredId()
    {
        // The family is matched on the stable handle; the numeric id only ever comes from that lookup.
        var stub = new MaxioStubHandler().WithFamily().WithSeededPlans();

        await ServiceOver(stub).GetPlansAsync();

        Assert.Equal(1, stub.CountOf(HttpMethod.Get, "/product_families.json"));
        Assert.Contains(stub.Requests, r => r.Path.Contains($"/product_families/{FamilyId}/products.json"));
    }

    [Fact]
    public async Task FailsWhenTheConfiguredFamilyHandleIsAbsent()
    {
        var stub = new MaxioStubHandler()
            .On(HttpMethod.Get, "/product_families.json", HttpStatusCode.OK,
                """[{"product_family":{"id":99,"handle":"someone-elses-family"}}]""");

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => ServiceOver(stub).GetPlansAsync());

        Assert.Equal(BillingFailureKind.NotConfigured, ex.Kind);
    }

    [Fact]
    public async Task AnUnreadableResponseIsAFailureNotAnEmptyCatalog()
    {
        // Reporting "no plans available" for a body we could not parse would be a confident wrong answer.
        var stub = new MaxioStubHandler().WithFamily()
            .WithPlans("""[{"product":{"price_in_cents":"not-a-number"}}]""");

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => ServiceOver(stub).GetPlansAsync());

        Assert.Equal(BillingFailureKind.ProviderUnavailable, ex.Kind);
    }

    [Fact]
    public async Task ProviderAuthFailureIsNotReflectedBackAsAClientError()
    {
        // A 401 means OUR credentials are wrong. Passing it through would blame the caller for our outage.
        var stub = new MaxioStubHandler()
            .On(HttpMethod.Get, "/product_families.json", HttpStatusCode.Unauthorized, """{"error":"bad key"}""");

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => ServiceOver(stub).GetPlansAsync());

        Assert.Equal(BillingFailureKind.ProviderUnavailable, ex.Kind);
        Assert.Equal(401, ex.ProviderStatusCode);
    }

    [Fact]
    public async Task DoesNotLeakProviderExceptionTextToCallers()
    {
        var stub = new MaxioStubHandler()
            .On(HttpMethod.Get, "/product_families.json", HttpStatusCode.InternalServerError,
                """{"stack":"MaxioAdvancedBilling.Internal.Boom at line 42"}""");

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => ServiceOver(stub).GetPlansAsync());

        Assert.DoesNotContain("MaxioAdvancedBilling", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("line 42", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnsEmptyWhenTheShopperWasNeverEnrolled()
    {
        var stub = new MaxioStubHandler().WithNoCustomer();

        var subscriptions = await ServiceOver(stub).GetSubscriptionsAsync(Subscriber);

        Assert.Empty(subscriptions);
        Assert.Equal(0, stub.CountOf(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task ReportsCurrentPeriodEndAsTheNextBillingDate()
    {
        // next_assessment_at diverges to a dunning retry time after a failed renewal, so reading it first
        // would show a retry time as the next bill for exactly the subscriptions people go and look at.
        var stub = new MaxioStubHandler().WithExistingCustomer()
            .WithCustomerSubscriptions(SubscriptionListJson());

        var subscription = Assert.Single(await ServiceOver(stub).GetSubscriptionsAsync(Subscriber));

        Assert.Equal(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero), subscription.NextBillingDate);
        Assert.Equal("active", subscription.State);
        Assert.True(subscription.IsLive);
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("expired")]
    [InlineData("failed_to_create")]
    public async Task TerminalStatesAreNotLive(string state)
    {
        var stub = new MaxioStubHandler().WithExistingCustomer()
            .WithCustomerSubscriptions(SubscriptionListJson(state: state));

        var subscription = Assert.Single(await ServiceOver(stub).GetSubscriptionsAsync(Subscriber));

        Assert.False(subscription.IsLive);
    }

    [Theory]
    [InlineData("past_due")]
    [InlineData("on_hold")]
    [InlineData("trial_ended")]
    [InlineData("some_state_we_have_never_seen")]
    public async Task NonTerminalAndUnknownStatesStayLive(string state)
    {
        // Over-reporting an enrollment is safe; under-reporting it would let a second subscription through.
        var stub = new MaxioStubHandler().WithExistingCustomer()
            .WithCustomerSubscriptions(SubscriptionListJson(state: state));

        var subscription = Assert.Single(await ServiceOver(stub).GetSubscriptionsAsync(Subscriber));

        Assert.True(subscription.IsLive);
    }
}
