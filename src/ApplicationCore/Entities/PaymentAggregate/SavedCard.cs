using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card saved at Finix for a shopper. Only the Finix payment-instrument id and non-sensitive
/// display data are stored here; the PAN and security code never reach this database.
/// </summary>
public class SavedCard
{
    /// <summary>Application-owned identifier returned to the shopper.</summary>
    public string Id { get; set; } = string.Empty;
    public string BuyerId { get; set; } = string.Empty;
    public string FinixPaymentInstrumentId { get; set; } = string.Empty;
    public string? BuyerIdentityId { get; set; }

    public string? Brand { get; set; }
    public string? LastFour { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? Name { get; set; }

    public bool Enabled { get; set; } = true;
    public bool Deleted { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
