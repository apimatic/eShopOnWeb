using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card or bank account a shopper saved at Stax. Only Stax identifiers and display data are kept; the
/// card or account number itself never reaches the shop's database.
/// Rows are kept after removal so that support can see the full history.
/// </summary>
public class SavedPaymentMethod
{
    public string StaxPaymentMethodId { get; set; } = string.Empty;
    public string BuyerId { get; set; } = string.Empty;
    public string StaxCustomerId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? LastFour { get; set; }
    public string? Nickname { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RemovedAt { get; set; }
}
