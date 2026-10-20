using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

// Provider-neutral shapes the gateway hands back. No SDK type leaves MaxioBillingGateway.

public sealed record BillingPlan(
    int? ProductId,
    string Handle,
    string? Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit);

/// <summary>The configured family's plans. <see cref="Truncated"/> is true when the page cap stopped the read early.</summary>
public sealed record PlanCatalog(IReadOnlyList<BillingPlan> Plans, bool Truncated);

public sealed record BillingCustomer(int Id, string? Reference);

public sealed record NewBillingCustomer(string Reference, string FirstName, string LastName, string Email);

public sealed record BillingSubscription(
    int Id,
    string? Reference,
    string State,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    DateTimeOffset? NextAssessmentAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ActivatedAt,
    string? PaymentCollectionMethod)
{
    /// <summary>The next billing date shown to the shopper.</summary>
    public DateTimeOffset? NextBillingAt => NextAssessmentAt ?? CurrentPeriodEndsAt;
}

/// <param name="Reference">Deterministic subscription reference sent with the write.</param>
/// <param name="CustomerId">The provider id of the caller's own customer.</param>
/// <param name="PlanHandle">A handle validated against the configured family's plan list.</param>
/// <param name="AttemptStartedAt">When this write attempt started; bounds the fallback match while settling an unknown outcome.</param>
public sealed record NewBillingSubscription(
    string Reference,
    int CustomerId,
    string PlanHandle,
    DateTimeOffset AttemptStartedAt);

public enum WriteOutcomeKind
{
    /// <summary>This write created the resource (directly, or confirmed by re-reading after an unknown outcome).</summary>
    Created,

    /// <summary>The provider refused the write but already holds the resource for the reference.</summary>
    AlreadyExisted,

    /// <summary>The provider definitively refused the write and holds nothing for the reference.</summary>
    Refused,

    /// <summary>The write may or may not have landed; the re-read could not settle it.</summary>
    Unknown
}

public sealed record WriteOutcome<T>(WriteOutcomeKind Kind, T? Value, IReadOnlyList<string> Messages, int? ProviderStatus = null)
    where T : class
{
    public static WriteOutcome<T> Created(T value) => new(WriteOutcomeKind.Created, value, Array.Empty<string>());
    public static WriteOutcome<T> AlreadyExisted(T value) => new(WriteOutcomeKind.AlreadyExisted, value, Array.Empty<string>());
    public static WriteOutcome<T> Refused(IReadOnlyList<string> messages, int? status) => new(WriteOutcomeKind.Refused, null, messages, status);
    public static WriteOutcome<T> Unknown() => new(WriteOutcomeKind.Unknown, null, Array.Empty<string>());
}
