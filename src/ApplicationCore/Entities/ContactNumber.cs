using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class ContactNumber : BaseEntity, IAggregateRoot
{
    private ContactNumber() { }

    public ContactNumber(string buyerId, string value, DateTimeOffset createdAt)
    {
        BuyerId = buyerId;
        Value = value;
        CreatedAt = createdAt;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsActive => DeletedAt is null;

    public void Delete(DateTimeOffset deletedAt) => DeletedAt ??= deletedAt;
}
