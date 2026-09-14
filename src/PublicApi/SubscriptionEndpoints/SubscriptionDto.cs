using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription owned by the signed-in user, mirrored from Maxio Advanced Billing.
/// </summary>
public class SubscriptionDto
{
    /// <summary>
    /// Maxio Advanced Billing subscription id.
    /// </summary>
    public int Id { get; set; }

    public string State { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>
    /// Price of the subscribed plan in the smallest currency unit (e.g. cents).
    /// </summary>
    public int ProductPriceInCents { get; set; }

    public string Currency { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>
    /// Date of the next billing assessment.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public int BalanceInCents { get; set; }
}
