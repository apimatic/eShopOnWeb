using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class SavedCard
{
    private SavedCard() { }

    public SavedCard(string shopperId, string finixPaymentInstrumentId, string? brand, string? lastFour,
        int? expirationMonth, int? expirationYear, string? requestKey = null)
    {
        Id = Guid.NewGuid().ToString("N");
        ShopperId = shopperId;
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
        Brand = brand;
        LastFour = lastFour;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        RequestKey = requestKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string Id { get; private set; } = string.Empty;
    public string ShopperId { get; private set; } = string.Empty;
    public string FinixPaymentInstrumentId { get; private set; } = string.Empty;
    public string? Brand { get; private set; }
    public string? LastFour { get; private set; }
    public int? ExpirationMonth { get; private set; }
    public int? ExpirationYear { get; private set; }
    public bool IsDeleted { get; private set; }
    public string? RequestKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void MarkDeleted()
    {
        IsDeleted = true;
    }
}
