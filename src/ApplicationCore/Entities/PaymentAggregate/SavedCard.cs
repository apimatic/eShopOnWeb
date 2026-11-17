using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A shopper-owned saved card. Only Finix identifiers and non-sensitive display metadata are kept.
/// </summary>
public class SavedCard
{
    public int Id { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string FinixIdentityId { get; set; } = string.Empty;
    public string PaymentInstrumentId { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
