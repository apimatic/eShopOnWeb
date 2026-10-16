using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>A recurring plan offered by the configured product family.</summary>
public sealed record SubscriptionPlan(
    int? Id,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit,
    long? TrialPriceInCents,
    int? TrialInterval,
    string? TrialIntervalUnit,
    long? InitialChargeInCents,
    bool RequiresPaymentMethod);

/// <summary>The plans on offer. <see cref="IsTruncated"/> is true when the listing stopped at its page cap.</summary>
public sealed record PlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool IsTruncated);

/// <summary>Who is subscribing, as known to eShop.</summary>
public sealed record BillingBuyer(string BuyerId, string Email, string? FirstName, string? LastName);

public sealed record BillingCustomerAccount(int Id, string? Reference);

public sealed record NewBillingCustomer(string Reference, string FirstName, string LastName, string Email);

public sealed record NewBillingSubscription(string Reference, string PlanHandle, int CustomerId);

/// <summary>A subscription as held by the billing system.</summary>
public sealed record BillingSubscription(
    int Id,
    string? Reference,
    string State,
    string? PlanHandle,
    string? PlanName,
    string? ProductFamilyHandle,
    long? PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    DateTimeOffset? CurrentPeriodStartedAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt,
    bool CancelAtEndOfPeriod)
{
    /// <summary>States after which the subscription will never bill again.</summary>
    public bool IsTerminal => State is "canceled" or "expired" or "failed_to_create";
}

public enum WriteOutcome
{
    /// <summary>The billing system holds the record (created now, or found by its reference).</summary>
    Succeeded,

    /// <summary>The billing system refused the request; nothing was created.</summary>
    Rejected,

    /// <summary>The call failed in a way that leaves it unknown whether the billing system acted.</summary>
    Unknown
}

public sealed record CustomerCreateResult(WriteOutcome Outcome, BillingCustomerAccount? Customer, IReadOnlyList<string> Errors)
{
    public static CustomerCreateResult Succeeded(BillingCustomerAccount customer) => new(WriteOutcome.Succeeded, customer, []);
    public static CustomerCreateResult Rejected(IReadOnlyList<string> errors) => new(WriteOutcome.Rejected, null, errors);
    public static CustomerCreateResult Unknown() => new(WriteOutcome.Unknown, null, []);
}

public sealed record SubscriptionCreateResult(WriteOutcome Outcome, BillingSubscription? Subscription, IReadOnlyList<string> Errors)
{
    public static SubscriptionCreateResult Succeeded(BillingSubscription subscription) => new(WriteOutcome.Succeeded, subscription, []);
    public static SubscriptionCreateResult Rejected(IReadOnlyList<string> errors) => new(WriteOutcome.Rejected, null, errors);
    public static SubscriptionCreateResult Unknown() => new(WriteOutcome.Unknown, null, []);
}

public enum SubscribeOutcome
{
    /// <summary>A new subscription was created.</summary>
    Created,

    /// <summary>The buyer already holds this plan; the existing subscription is returned (idempotent replay).</summary>
    AlreadySubscribed,

    /// <summary>The request was sent but its result could not be confirmed yet; it is settled in the background.</summary>
    PendingConfirmation
}

public sealed record SubscribeResult(SubscribeOutcome Outcome, string PlanHandle, string Reference, BillingSubscription? Subscription);

/// <summary>An enrollment whose billing outcome is not confirmed yet.</summary>
public sealed record PendingEnrollment(string PlanHandle, string Reference, DateTimeOffset RequestedAt);

public sealed record BuyerSubscriptions(IReadOnlyList<BillingSubscription> Subscriptions, IReadOnlyList<PendingEnrollment> Pending);
