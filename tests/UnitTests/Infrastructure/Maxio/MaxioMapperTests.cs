using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioMapperTests
{
    [Theory]
    [InlineData("active", SubscriptionState.Active)]
    [InlineData("past_due", SubscriptionState.PastDue)]
    [InlineData("trial_ended", SubscriptionState.TrialEnded)]
    [InlineData("failed_to_create", SubscriptionState.FailedToCreate)]
    public void MapsTheSpecStateStrings(string raw, SubscriptionState expected)
    {
        Assert.Equal(expected, MaxioMapper.ParseState(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("some_future_state")]
    public void FallsBackToUnknownForAStateItDoesNotRecognise(string? raw)
    {
        Assert.Equal(SubscriptionState.Unknown, MaxioMapper.ParseState(raw));
    }

    [Theory]
    [InlineData(SubscriptionState.Active, true)]
    [InlineData(SubscriptionState.PastDue, true)]
    [InlineData(SubscriptionState.Trialing, true)]
    [InlineData(SubscriptionState.Canceled, false)]
    [InlineData(SubscriptionState.Expired, false)]
    [InlineData(SubscriptionState.TrialEnded, false)]
    public void ClassifiesEndOfLifeStatesAsNotLive(SubscriptionState state, bool expected)
    {
        Assert.Equal(expected, state.IsLive());
    }

    [Fact]
    public void TreatsAnUnrecognisedStateAsLiveSoItNeverBillsTwice()
    {
        Assert.True(SubscriptionState.Unknown.IsLive());
    }

    [Fact]
    public void KeepsTheRawStateForDiagnostics()
    {
        var mapped = MaxioMapper.ToSubscription(
            new MaxioSubscription { Id = 1, State = "brand_new_state", CreatedAt = DateTimeOffset.UtcNow },
            "USD");

        Assert.Equal("brand_new_state", mapped.RawState);
        Assert.Equal(SubscriptionState.Unknown, mapped.State);
    }

    [Fact]
    public void SurfacesNextAssessmentAsTheNextBillingDate()
    {
        var next = DateTimeOffset.UtcNow.AddDays(30);

        var mapped = MaxioMapper.ToSubscription(
            new MaxioSubscription { Id = 1, State = "active", NextAssessmentAt = next, CreatedAt = DateTimeOffset.UtcNow },
            "USD");

        Assert.Equal(next, mapped.NextBillingAt);
    }

    [Fact]
    public void FallsBackToTheSiteCurrencyWhenTheSubscriptionOmitsOne()
    {
        var mapped = MaxioMapper.ToSubscription(
            new MaxioSubscription { Id = 1, State = "active", Currency = null, CreatedAt = DateTimeOffset.UtcNow },
            "EUR");

        Assert.Equal("EUR", mapped.Currency);
    }

    [Fact]
    public void MapsAProductWithoutATrialToAPlanWithoutOne()
    {
        var plan = MaxioMapper.ToPlan(
            new MaxioProduct
            {
                Id = 7,
                Handle = "eshop-pro",
                Name = "Pro Plan",
                PriceInCents = 29900,
                Interval = 1,
                IntervalUnit = "month",
                TrialInterval = 0,
                InitialChargeInCents = 0,
                RequireCreditCard = false,
                ProductFamily = new MaxioProductFamily { Handle = "eshop-subscribe" }
            },
            "USD");

        Assert.Null(plan.Trial);
        Assert.Null(plan.SetupFeeInCents);
        Assert.Equal("eshop-subscribe", plan.ProductFamilyHandle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("every month", plan.Interval.ToString());
    }
}
