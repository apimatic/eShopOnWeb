using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class SavedCard : BaseEntity, IAggregateRoot
{
    public string OwnerId { get; private set; }
    public string FinixPaymentInstrumentId { get; private set; }
    public string? Brand { get; private set; }
    public string Last4 { get; private set; }
    public int ExpirationMonth { get; private set; }
    public int ExpirationYear { get; private set; }
    public string? Name { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private SavedCard()
    {
        OwnerId = string.Empty;
        FinixPaymentInstrumentId = string.Empty;
        Last4 = string.Empty;
    }

    public SavedCard(string ownerId, string finixPaymentInstrumentId, string? brand, string last4,
        int expirationMonth, int expirationYear, string? name)
    {
        OwnerId = ownerId;
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
        Brand = brand;
        Last4 = last4;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        Name = name;
    }

    public void Delete() => IsDeleted = true;
}
