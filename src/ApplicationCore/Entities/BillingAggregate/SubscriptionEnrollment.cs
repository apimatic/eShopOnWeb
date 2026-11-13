using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// One subscription a user asked for. <see cref="BillingClaim.Reference"/> is derived from
/// (customer reference, plan handle), so a repeated "subscribe to plan X" maps to the same row.
/// </summary>
public class SubscriptionEnrollment : BillingClaim
{
    public SubscriptionEnrollment(string reference, string customerReference, string planHandle, DateTime nowUtc, TimeSpan lease)
        : base(reference, nowUtc, lease)
    {
        CustomerReference = Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));
        PlanHandle = Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
    }

#pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionEnrollment() { }
#pragma warning restore CS8618

    public string CustomerReference { get; private set; }

    public string PlanHandle { get; private set; }
}
