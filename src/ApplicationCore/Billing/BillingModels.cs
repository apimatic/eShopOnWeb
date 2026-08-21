using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record BillingUser(string UserId, string Email, string FirstName, string LastName);

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string Currency);

public sealed record SubscriptionConfirmation(
    string Reference,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingDate);

public sealed record SubscribeOutcome(SubscriptionConfirmation? Subscription, bool Created, bool InProgress);

public enum BillingLinkStatus
{
    Pending,
    Completed,
    RetryableFailure,
    TerminalFailure
}

public enum BillingClaimStatus
{
    Acquired,
    Completed,
    InProgress,
    TerminalFailure
}

public sealed record CustomerClaim(BillingClaimStatus Status, string Reference, string? LeaseId);

public sealed record SubscriptionClaim(
    BillingClaimStatus Status,
    string Reference,
    string? LeaseId,
    SubscriptionConfirmation? Confirmation);

