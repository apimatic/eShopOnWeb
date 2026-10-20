using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>A plan offered for subscription (a non-archived product of the configured Maxio product family).</summary>
public sealed record BillingPlan(
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    bool? RequiresPaymentMethod);

/// <summary>The plan catalogue. <see cref="Truncated"/> is true when the page cap stopped the walk before the end.</summary>
public sealed record PlanCatalog(IReadOnlyList<BillingPlan> Plans, bool Truncated);

public sealed record BillingCustomer(int Id, string? Reference);

/// <summary>A subscription as read back from Maxio. Nullable members are values Maxio did not send.</summary>
public sealed record BillingSubscription(
    int Id,
    string? Reference,
    string State,
    bool IsLive,
    string? PlanHandle,
    string? PlanName,
    string? ProductFamilyHandle,
    long? PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ActivatedAt);

/// <summary>
/// Result of a write whose outcome may be unknown. Either <see cref="Value"/> is set (the write is known to have
/// landed — created now, or found by re-reading), or <see cref="OutcomeUnknown"/> is true (the connection failed
/// and the re-read could not establish what happened; the claim stays Pending for later settlement).
/// </summary>
public sealed record WriteResult<T>(T? Value, bool OutcomeUnknown) where T : class
{
    public static WriteResult<T> Landed(T value) => new(value, false);
    public static WriteResult<T> Unknown() => new(null, true);
}

/// <summary>Outcome of re-reading Maxio for a write we may or may not have made.</summary>
public sealed record Reconciliation<T>(T? Found, bool LookupSucceeded) where T : class
{
    /// <summary>True when Maxio answered and holds no such record — the write provably did not land (yet).</summary>
    public bool ProvenAbsent => LookupSucceeded && Found is null;
}
