using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A row whose primary key is the claim itself. Inserting a second row with the same key is refused
/// by the store, which is what stops two callers from running the same payment operation at once.
/// </summary>
public class PaymentClaim
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentClaim() { }

    public PaymentClaim(string key, Guid owner, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        Key = key;
        Owner = owner;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public string Key { get; private set; }
    public Guid Owner { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
}
