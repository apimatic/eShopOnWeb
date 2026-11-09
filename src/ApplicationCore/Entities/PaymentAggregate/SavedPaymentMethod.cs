using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Shop-side record of a card or bank account a shopper saved. Never holds card or account numbers,
/// only what is safe to display. The authoritative state of every method lives at Stax.
/// </summary>
public class SavedPaymentMethod
{
    /// <summary>The Stax payment method id.</summary>
    public string Id { get; set; } = string.Empty;
    public string BuyerEmail { get; set; } = string.Empty;
    public string StaxCustomerId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
}
