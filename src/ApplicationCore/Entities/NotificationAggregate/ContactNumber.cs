using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can reach them by SMS. The stored
/// <see cref="PhoneNumber"/> is always the provider's canonical E.164 form of the number, validated
/// at registration time — never whatever the caller happened to type.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }

    public ContactNumber(string ownerId, string phoneNumber)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        OwnerId = ownerId;
        PhoneNumber = phoneNumber;
    }

    /// <summary>Identity of the shopper who registered the number. Used to scope every read/use/delete.</summary>
    public string OwnerId { get; private set; }

    /// <summary>The provider's canonical E.164 representation of the number.</summary>
    public string PhoneNumber { get; private set; }
}
