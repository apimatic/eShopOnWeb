using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A request to enroll an eShopOnWeb user onto a plan.
/// </summary>
public class SubscribeRequest
{
    public SubscribeRequest(BillingCustomerProfile customer, string planHandle,
        string? pricePointHandle = null, string? idempotencyKey = null)
    {
        Customer = Guard.Against.Null(customer, nameof(customer));
        PlanHandle = Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        PricePointHandle = string.IsNullOrWhiteSpace(pricePointHandle) ? null : pricePointHandle.Trim();
        IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
    }

    public BillingCustomerProfile Customer { get; }

    /// <summary>Handle of the plan to subscribe to. Handles are stable; provider ids are not.</summary>
    public string PlanHandle { get; }

    /// <summary>Optional non-default price point on the plan.</summary>
    public string? PricePointHandle { get; }

    /// <summary>
    /// Optional caller-supplied key that makes a retry of the same logical signup safe.
    /// When omitted, one is derived from the user and plan.
    /// </summary>
    public string? IdempotencyKey { get; }
}
