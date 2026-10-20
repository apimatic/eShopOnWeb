using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>The authenticated shopper a billing call is made for. Built from the JWT, never from the request body.</summary>
public sealed record BillingCaller(string UserName, string? Email);

/// <summary>
/// The plans offered for subscription. <see cref="IsPartial"/> is true when the page cap was reached
/// before Maxio returned a short page, i.e. there may be plans that are not listed.
/// </summary>
public sealed record PlanCatalog(IReadOnlyList<SubscriptionPlanDto> Plans, bool IsPartial);

public enum SubscribeOutcome
{
    /// <summary>This request created the subscription at Maxio.</summary>
    Created,

    /// <summary>The caller already had a subscription to this plan; it is returned unchanged.</summary>
    Existing,

    /// <summary>The create request may or may not have landed at Maxio; it is settled by a later request.</summary>
    Unknown
}

public sealed record SubscribeResult(SubscribeOutcome Outcome, SubscriptionDto Subscription);
