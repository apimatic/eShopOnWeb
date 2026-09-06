using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.UnitTests.Builders;

/// <summary>Builds subscription domain objects for tests without repeating long constructor calls.</summary>
public class SubscriptionBuilder
{
    private long _id = 1;
    private string? _reference = "test-reference";
    private SubscriptionState _state = SubscriptionState.Active;
    private string _rawState = "active";
    private string? _planHandle = "eshop-pro";
    private long _customerId = 100;
    private DateTimeOffset? _createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private DateTimeOffset? _nextAssessmentAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

    public SubscriptionBuilder WithId(long id)
    {
        _id = id;
        return this;
    }

    public SubscriptionBuilder WithReference(string? reference)
    {
        _reference = reference;
        return this;
    }

    public SubscriptionBuilder WithState(SubscriptionState state, string rawState)
    {
        _state = state;
        _rawState = rawState;
        return this;
    }

    public SubscriptionBuilder WithPlanHandle(string? planHandle)
    {
        _planHandle = planHandle;
        return this;
    }

    public SubscriptionBuilder WithCustomerId(long customerId)
    {
        _customerId = customerId;
        return this;
    }

    public SubscriptionBuilder WithCreatedAt(DateTimeOffset createdAt)
    {
        _createdAt = createdAt;
        return this;
    }

    public CustomerSubscription Build() => new(
        id: _id,
        reference: _reference,
        state: _state,
        rawState: _rawState,
        planHandle: _planHandle,
        planName: "Pro Plan",
        productPriceInCents: 29900,
        currency: "USD",
        interval: 1,
        intervalUnit: "month",
        customerId: _customerId,
        customerReference: "eshoponweb-shopper@example.com",
        currentPeriodStartedAt: _createdAt,
        currentPeriodEndsAt: _nextAssessmentAt,
        nextAssessmentAt: _nextAssessmentAt,
        trialStartedAt: null,
        trialEndedAt: null,
        activatedAt: _createdAt,
        canceledAt: null,
        expiresAt: null,
        createdAt: _createdAt,
        cancelAtEndOfPeriod: false,
        paymentCollectionMethod: "remittance",
        balanceInCents: 0);
}
