using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class ContactNumber : BaseEntity, IAggregateRoot
{
    private ContactNumber() { }

    public ContactNumber(string buyerId, string phoneNumber, DateTimeOffset createdAt)
    {
        BuyerId = buyerId;
        PhoneNumber = phoneNumber;
        CreatedAt = createdAt;
    }

    public string BuyerId { get; private set; } = null!;
    public string PhoneNumber { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
}
