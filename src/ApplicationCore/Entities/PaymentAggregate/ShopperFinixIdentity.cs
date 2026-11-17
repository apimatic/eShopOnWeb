using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Maps an eShop shopper to the Finix buyer <c>Identity</c> that owns that
/// shopper's payment instruments. A row is inserted as a claim before the
/// identity is created at Finix, then completed with the returned id.
/// </summary>
public class ShopperFinixIdentity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private ShopperFinixIdentity() {}

    public ShopperFinixIdentity(string shopperId, string email)
    {
        Guard.Against.NullOrEmpty(shopperId, nameof(shopperId));
        Guard.Against.NullOrEmpty(email, nameof(email));

        ShopperId = shopperId;
        Email = email;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string ShopperId { get; private set; }
    public string Email { get; private set; }
    public string? FinixIdentityId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsPending => string.IsNullOrWhiteSpace(FinixIdentityId);

    public void Complete(string finixIdentityId)
    {
        Guard.Against.NullOrEmpty(finixIdentityId, nameof(finixIdentityId));
        FinixIdentityId = finixIdentityId;
    }
}
