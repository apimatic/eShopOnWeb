using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.UnitTests.Builders;

public class SubscriptionBuilder
{
    public const string TestBuyerId = "buyer@example.com";
    public const int TestSubscriptionId = 42;
    public const string TestProductHandle = "eshop-pro";
    public const string TestOtherProductHandle = "basic-plan";

    public Subscription WithState(string state) => new(
        TestSubscriptionId,
        customerId: 1,
        customerReference: TestBuyerId,
        productHandle: TestProductHandle,
        productName: "Pro Plan",
        priceInCents: 29900,
        state: state,
        currentPeriodStartedAt: DateTimeOffset.UtcNow.AddDays(-10),
        currentPeriodEndsAt: DateTimeOffset.UtcNow.AddDays(20),
        nextAssessmentAt: DateTimeOffset.UtcNow.AddDays(20),
        cancelAtEndOfPeriod: false,
        scheduledCancellationAt: null,
        activatedAt: DateTimeOffset.UtcNow.AddDays(-10),
        createdAt: DateTimeOffset.UtcNow.AddDays(-10));

    public Subscription WithBuyerId(string buyerId, string state = "active") => new(
        TestSubscriptionId,
        customerId: 1,
        customerReference: buyerId,
        productHandle: TestProductHandle,
        productName: "Pro Plan",
        priceInCents: 29900,
        state: state,
        currentPeriodStartedAt: DateTimeOffset.UtcNow.AddDays(-10),
        currentPeriodEndsAt: DateTimeOffset.UtcNow.AddDays(20),
        nextAssessmentAt: DateTimeOffset.UtcNow.AddDays(20),
        cancelAtEndOfPeriod: false,
        scheduledCancellationAt: null,
        activatedAt: DateTimeOffset.UtcNow.AddDays(-10),
        createdAt: DateTimeOffset.UtcNow.AddDays(-10));

    public Subscription Active() => WithState("active");
}
