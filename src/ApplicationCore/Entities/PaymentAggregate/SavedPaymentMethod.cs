using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A shopper's saved card. Only the Finix instrument id and non-sensitive descriptors are stored;
/// the PAN and security code are never persisted.
/// </summary>
public class SavedPaymentMethod
{
    public string BuyerId { get; set; } = string.Empty;
    public string CardFingerprint { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string FinixPaymentInstrumentId { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string LastFour { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
