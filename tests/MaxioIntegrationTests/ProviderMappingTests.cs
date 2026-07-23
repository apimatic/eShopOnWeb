using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// How the provider's vocabulary is translated into the domain's, and how the client addresses the
/// product family. Both are places where a silent mistranslation would be invisible in production.
/// </summary>
public class ProviderMappingTests
{
    [Theory]
    [InlineData("active", nameof(SubscriptionState.Active))]
    [InlineData("trialing", nameof(SubscriptionState.Trialing))]
    [InlineData("pending", nameof(SubscriptionState.Pending))]
    [InlineData("assessing", nameof(SubscriptionState.Assessing))]
    [InlineData("past_due", nameof(SubscriptionState.PastDue))]
    [InlineData("soft_failure", nameof(SubscriptionState.SoftFailure))]
    [InlineData("suspended", nameof(SubscriptionState.Suspended))]
    [InlineData("canceled", nameof(SubscriptionState.Canceled))]
    [InlineData("expired", nameof(SubscriptionState.Expired))]
    [InlineData("paused", nameof(SubscriptionState.Paused))]
    [InlineData("unpaid", nameof(SubscriptionState.Unpaid))]
    [InlineData("trial_ended", nameof(SubscriptionState.TrialEnded))]
    [InlineData("on_hold", nameof(SubscriptionState.OnHold))]
    [InlineData("awaiting_signup", nameof(SubscriptionState.AwaitingSignup))]
    [InlineData("failed_to_create", nameof(SubscriptionState.FailedToCreate))]
    public async Task EveryDocumentedProviderStateMapsOntoItsDomainState(string providerState, string expected)
    {
        var context = new MaxioTestContext();
        context.Server.MapGet("subscriptions/1.json", FakeResponse.Ok($$"""
            { "subscription": { "id": 1, "state": "{{providerState}}",
              "customer": { "id": 1, "reference": "u" }, "product": { "handle": "eshop-pro" } } }
            """));

        var subscription = await context.Client.GetSubscriptionAsync(1);

        Assert.Equal(Enum.Parse<SubscriptionState>(expected), subscription!.State);
    }

    [Theory]
    [InlineData("active", true)]
    [InlineData("trialing", true)]
    [InlineData("on_hold", false)]
    [InlineData("canceled", false)]
    [InlineData("past_due", false)]
    public async Task OnlyLiveStatesCountAsActiveForBillingDecisions(string providerState, bool expectedActive)
    {
        var context = new MaxioTestContext();
        context.Server.MapGet("subscriptions/1.json", FakeResponse.Ok($$"""
            { "subscription": { "id": 1, "state": "{{providerState}}",
              "customer": { "id": 1, "reference": "u" }, "product": { "handle": "eshop-pro" } } }
            """));

        var subscription = await context.Client.GetSubscriptionAsync(1);

        Assert.Equal(expectedActive, subscription!.IsActive);
    }

    [Fact]
    public async Task TheFamilyIsAddressedByIdWhenNoHandleIsConfigured()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "k",
            Subdomain = "cp-exp-3",
            ProductFamilyId = MaxioTestContext.ProductFamilyId,
            MeteredComponentHandle = "api-call"
        };
        var context = new MaxioTestContext(settings);
        context.Server.MapGet($"product_families/{MaxioTestContext.ProductFamilyId}/products.json",
            FakeResponse.Ok(MaxioPayloads.PlanList));

        var plans = await context.Client.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal(1, context.Server.CountRequests(HttpMethod.Get,
            $"product_families/{MaxioTestContext.ProductFamilyId}/products.json"));
    }

    [Fact]
    public async Task AnUnconfiguredFamilyIsReportedRatherThanCallingAMalformedUrl()
    {
        var settings = new MaxioSettings { ApiKey = "k", Subdomain = "cp-exp-3" };
        var context = new MaxioTestContext(settings);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => context.Client.ListPlansAsync());

        Assert.Contains("ProductFamilyHandle", exception.Message);
        Assert.Empty(context.Server.Requests);
    }

    [Fact]
    public void TheConfiguredMeteredComponentHandleIsExposedOnTheSeam()
    {
        var context = new MaxioTestContext();

        // The domain reports usage without naming a provider-specific component itself.
        Assert.Equal("api-call", context.Client.MeteredComponentHandle);
    }

    [Fact]
    public async Task AProviderErrorThatIsNotJsonStillSurfacesAsATypedException()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(MaxioTestContext.PlansRoute,
            new FakeResponse(System.Net.HttpStatusCode.BadGateway, "<html>gateway timeout</html>"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => context.Client.ListPlansAsync());

        Assert.Equal(502, exception.StatusCode);
        Assert.Contains("gateway timeout", exception.Message);
    }
}
