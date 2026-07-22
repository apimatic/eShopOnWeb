using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeResponse : BaseResponse
{
    public PlanChangeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PlanChangeResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }
    public string PreviousPlanHandle { get; set; } = string.Empty;
    public string NewPlanHandle { get; set; } = string.Empty;
    public string Timing { get; set; } = string.Empty;

    /// <summary>The amount actually applied, in major currency units.</summary>
    public decimal PaymentDue { get; set; }

    /// <summary>The amount actually applied, in the minor units the provider reports.</summary>
    public long AmountDueInCents { get; set; }

    /// <summary>The same figure as <see cref="AmountDueInCents"/>, under the payment-due name.</summary>
    public long PaymentDueInCents { get; set; }

    /// <summary>When the change takes effect. Null means it already has.</summary>
    public DateTimeOffset? EffectiveAt { get; set; }
}
