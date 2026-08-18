using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

/// <summary>
/// A mobile number a shopper has put on file so the shop can text them. The number stored is always the
/// messaging provider's own canonical E.164 form (obtained at registration), never the raw caller input.
/// A number belongs to exactly one shopper — endpoints must scope by <see cref="OwnerId"/>.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() { }
#pragma warning restore CS8618

    public ContactNumber(string ownerId, string phoneNumber)
    {
        OwnerId = Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        PhoneNumber = Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));
    }

    /// <summary>Identity (username) of the shopper who registered this number.</summary>
    public string OwnerId { get; private set; }

    /// <summary>The provider's canonical E.164 form of the number. This is the sending destination.</summary>
    public string PhoneNumber { get; private set; }
}
