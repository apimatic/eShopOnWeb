using System;

using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card or bank account a shopper saved at Stax. Removed methods are kept (with RemovedAt) so support can see them.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    public string ShopperEmail { get; set; } = string.Empty;
    public string StaxPaymentMethodId { get; set; } = string.Empty;
    /// <summary>"card" or "bank"</summary>
    public string Kind { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RemovedAt { get; set; }
}
