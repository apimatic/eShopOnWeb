using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A shopper's saved card. Only the Finix payment-instrument id and non-sensitive
/// display data are stored.
/// </summary>
public class SavedCard : BaseEntity
{
    public string BuyerId { get; set; } = string.Empty;
    public string FinixIdentityId { get; set; } = string.Empty;
    public string PaymentInstrumentId { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? Fingerprint { get; set; }
    public bool Removed { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RemovedAt { get; set; }
}
