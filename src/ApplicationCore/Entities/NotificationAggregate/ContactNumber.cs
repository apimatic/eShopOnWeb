using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them. The stored value is the
/// provider's own canonical form of the number (E.164), not whatever the caller typed. A number
/// belongs to the shopper who registered it.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string buyerId, string phoneNumber)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        BuyerId = buyerId;
        PhoneNumber = phoneNumber;
    }

    /// <summary>Owner of the number; holds the shopper's username. Scopes all access.</summary>
    public string BuyerId { get; private set; }

    /// <summary>Canonical E.164 number as returned by the provider. Persisted but never logged.</summary>
    public string PhoneNumber { get; private set; }
}
