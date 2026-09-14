using AdvancedBilling.Standard.Models;
using Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Subscriptions;

/// <summary>
/// The API surfaces Maxio's own vocabulary rather than inventing a second set of names, so the
/// translation between the SDK's generated enums and their wire values has to be exact.
/// </summary>
public class MaxioEnumTests
{
    [Theory]
    [InlineData(SubscriptionState.Active, "active")]
    [InlineData(SubscriptionState.PastDue, "past_due")]
    [InlineData(SubscriptionState.TrialEnded, "trial_ended")]
    [InlineData(SubscriptionState.FailedToCreate, "failed_to_create")]
    public void SubscriptionStatesUseMaxioWireNames(SubscriptionState state, string expected)
    {
        Assert.Equal(expected, MaxioEnum.ToWireName(state));
    }

    [Theory]
    [InlineData(CollectionMethod.Remittance, "remittance")]
    [InlineData(CollectionMethod.Automatic, "automatic")]
    [InlineData(CollectionMethod.Invoice, "invoice")]
    [InlineData(CollectionMethod.Prepaid, "prepaid")]
    public void CollectionMethodsUseMaxioWireNames(CollectionMethod method, string expected)
    {
        Assert.Equal(expected, MaxioEnum.ToWireName(method));
    }

    [Fact]
    public void NullableValuesMapToNull()
    {
        Assert.Null(MaxioEnum.ToWireName<SubscriptionState>(null));
        Assert.Equal("active", MaxioEnum.ToWireName<SubscriptionState>(SubscriptionState.Active));
    }

    [Theory]
    [InlineData("remittance", CollectionMethod.Remittance)]
    [InlineData("REMITTANCE", CollectionMethod.Remittance)]
    [InlineData(" automatic ", CollectionMethod.Automatic)]
    public void ConfiguredCollectionMethodsParseLeniently(string configured, CollectionMethod expected)
    {
        Assert.Equal(expected, MaxioEnum.FromWireName<CollectionMethod>(configured));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("cheque-in-the-post")]
    public void UnrecognisedCollectionMethodsParseToNull(string? configured)
    {
        Assert.Null(MaxioEnum.FromWireName<CollectionMethod>(configured));
    }

    [Fact]
    public void WireNamesOfListsEveryMember()
    {
        Assert.Equal(
            new[] { "automatic", "remittance", "prepaid", "invoice" },
            MaxioEnum.WireNamesOf<CollectionMethod>());
    }
}
