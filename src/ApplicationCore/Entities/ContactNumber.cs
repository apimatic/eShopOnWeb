using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A mobile number a shopper has registered so the shop can send them
/// order notifications by SMS. The PhoneNumber is stored in the provider's
/// canonical (E.164) form.
/// </summary>
public class ContactNumber : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private ContactNumber() {}

    public ContactNumber(string ownerId, string phoneNumber)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        OwnerId = ownerId;
        PhoneNumber = phoneNumber;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string OwnerId { get; private set; }
    public string PhoneNumber { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
