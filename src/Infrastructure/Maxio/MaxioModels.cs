using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A subscribable plan: a product of the configured Maxio product family.
/// </summary>
public class MaxioPlan
{
    public int ProductId { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; } = 1;
    public string IntervalUnit { get; set; } = "month";
}

/// <summary>
/// A Maxio Advanced Billing customer mirrored from an eShopOnWeb user.
/// </summary>
public class MaxioCustomer
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

/// <summary>
/// A subscription in Maxio Advanced Billing.
/// </summary>
public class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public int CustomerId { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
}

/// <summary>
/// Request to subscribe a customer (identified by its reference) to a plan.
/// </summary>
public class MaxioSubscribeRequest
{
    /// <summary>
    /// Unique reference of the Maxio customer for the eShopOnWeb user.
    /// </summary>
    public string CustomerReference { get; set; } = string.Empty;

    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerFirstName { get; set; } = string.Empty;
    public string CustomerLastName { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the plan (Maxio product) to subscribe to.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Unique reference for the subscription itself; identifies the
    /// (user, plan) pair so repeats of the same request are idempotent.
    /// </summary>
    public string SubscriptionReference { get; set; } = string.Empty;
}

/// <summary>
/// Outcome of a subscribe request.
/// </summary>
public class MaxioSubscribeResult
{
    public MaxioSubscription Subscription { get; set; } = new();

    /// <summary>
    /// True when an active subscription for the same (customer, plan) already
    /// existed and no new one was created.
    /// </summary>
    public bool AlreadySubscribed { get; set; }

    /// <summary>
    /// The Maxio customer backing the subscription (created on first use).
    /// </summary>
    public MaxioCustomer Customer { get; set; } = new();
}
