using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Cross-request claim for saving a card. Keyed by shopper + a keyed hash of the card, so a
/// double submit is rejected by the primary key before any Finix instrument is created.
/// </summary>
public class SavedCardClaim
{
    public string BuyerId { get; set; } = string.Empty;
    public string CardFingerprint { get; set; } = string.Empty;
    public string? PaymentMethodId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
