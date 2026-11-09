using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>Shop-side record of a card or bank account a shopper saved at Stax (no account numbers are kept).</summary>
public class SavedPaymentMethod : BaseEntity
{
    public string BuyerEmail { get; set; } = string.Empty;
    public string StaxPaymentMethodId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty; // card | bank
    public string Last4 { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RemovedAt { get; set; }
}
