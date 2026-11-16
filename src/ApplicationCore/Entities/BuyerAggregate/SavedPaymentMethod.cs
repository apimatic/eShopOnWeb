using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A Finix-owned payment instrument saved for a shopper. Only safe display data
/// (brand, last four, expiry) is persisted here; card numbers never are.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string finixPaymentInstrumentId, string? finixIdentityId,
        string? brand, string? last4, int expirationMonth, int expirationYear, string? cardholderName,
        DateTimeOffset createdAt)
    {
        BuyerId = buyerId;
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
        FinixIdentityId = finixIdentityId;
        Brand = brand;
        Last4 = last4;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        CardholderName = cardholderName;
        CreatedAt = createdAt;
    }

    public string BuyerId { get; private set; }
    public string FinixPaymentInstrumentId { get; private set; }
    public string? FinixIdentityId { get; private set; }
    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }
    public int ExpirationMonth { get; private set; }
    public int ExpirationYear { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
