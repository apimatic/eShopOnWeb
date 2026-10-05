using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// An atomic "I am doing this" marker for one payment action. Its <see cref="Key"/> is the primary key,
/// so the store itself refuses a second claim for the same action (double click, concurrent caller).
/// </summary>
public class PaymentClaim
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentClaim() { }

    public PaymentClaim(string key, DateTimeOffset now)
    {
        Key = key;
        ClaimedAt = now;
    }

    public string Key { get; private set; }
    public DateTimeOffset ClaimedAt { get; private set; }
}
