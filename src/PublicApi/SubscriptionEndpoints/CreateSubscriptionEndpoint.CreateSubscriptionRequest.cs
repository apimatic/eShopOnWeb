using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated user to a plan.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (see GET api/subscription-plans).</summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional caller-supplied idempotency key. Retrying with the same key is
    /// rejected by the billing system as a duplicate instead of enrolling twice.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// A subscription as exposed over the wire.
/// </summary>
public class SubscriptionSummaryDto
{
    /// <summary>Subscription ID in the billing system of record.</summary>
    public int SubscriptionId { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>Billing-system state, e.g. active, trialing, past_due, canceled.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Recurring price, in integer cents.</summary>
    public long PriceCents { get; set; }

    /// <summary>Recurring price, formatted (e.g. "299.00").</summary>
    public string Price { get; set; } = string.Empty;

    /// <summary>Next scheduled billing date (end of the current period).</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Response confirming an enrollment: plan, price, state, next billing date.
/// </summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionSummaryDto Subscription { get; set; } = new();

    /// <summary>The billing customer ID (Maxio customer) for the authenticated user.</summary>
    public int BillingCustomerId { get; set; }

    /// <summary>The durable user reference stored on the billing customer (the eShop user ID).</summary>
    public string BillingCustomerReference { get; set; } = string.Empty;

    /// <summary>True when the user already had a live subscription on this plan.</summary>
    public bool AlreadySubscribed { get; set; }
}
