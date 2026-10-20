using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingClaimAggregate;

/// <summary>
/// Claim for creating one subscription of one customer to one plan.
/// <see cref="BillingClaim.Reference"/> is derived from the customer reference and the plan handle.
/// </summary>
public class BillingSubscriptionClaim : BillingClaim
{
#pragma warning disable CS8618 // Required by Entity Framework
    private BillingSubscriptionClaim() { }
#pragma warning restore CS8618

    public BillingSubscriptionClaim(string reference, string customerReference, string planHandle, DateTimeOffset now)
        : base(reference, now)
    {
        CustomerReference = Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));
        PlanHandle = Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
    }

    public string CustomerReference { get; private set; }

    public string PlanHandle { get; private set; }
}
