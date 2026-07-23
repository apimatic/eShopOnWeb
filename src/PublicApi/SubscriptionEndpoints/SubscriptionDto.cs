using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    /// <summary>The billing provider's subscription id.</summary>
    public int Id { get; set; }

    public int CustomerId { get; set; }

    /// <summary>The eShopOnWeb username the provider customer is keyed on.</summary>
    public string? CustomerReference { get; set; }

    public SubscriptionPlanDto? Plan { get; set; }

    /// <summary>The lifecycle state as this application models it.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>The verbatim state reported by the billing provider.</summary>
    public string ProviderState { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next invoice is assessed — the customer-facing next billing date.</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public bool CancelAtEndOfPeriod { get; set; }
    public DateTimeOffset? DelayedCancelAt { get; set; }

    /// <summary>The plan this subscription moves to at the next renewal, when a change is scheduled.</summary>
    public string? PendingPlanHandle { get; set; }

    /// <summary>The outstanding balance in major units.</summary>
    public decimal Balance { get; set; }
}
