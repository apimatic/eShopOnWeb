using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The shopper's subscription as recorded in Maxio Advanced Billing.
/// </summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public int SubscriptionId { get; set; }

    public bool CreatedNew { get; set; }

    public int MaxioCustomerId { get; set; }

    public string State { get; set; } = string.Empty;

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public decimal? Price { get; set; }

    public int? PriceInCents { get; set; }

    public string? Currency { get; set; }

    public DateTime? NextBillingDateUtc { get; set; }

    public DateTime? CreatedAtUtc { get; set; }
}
