using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A short-lived claim that makes a payment write at-most-once. The primary key refuses a
/// second caller before the Finix call is made.
/// </summary>
public class PaymentClaim
{
    private PaymentClaim() { }

    public PaymentClaim(string claimKey)
    {
        ClaimKey = claimKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string ClaimKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
