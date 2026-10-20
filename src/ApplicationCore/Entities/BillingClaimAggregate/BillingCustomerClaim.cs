using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingClaimAggregate;

/// <summary>
/// Claim for creating the billing-provider customer that represents one shop user.
/// <see cref="BillingClaim.Reference"/> is the deterministic, PII-free customer reference.
/// </summary>
public class BillingCustomerClaim : BillingClaim
{
    private BillingCustomerClaim() { }

    public BillingCustomerClaim(string reference, DateTimeOffset now) : base(reference, now) { }
}
