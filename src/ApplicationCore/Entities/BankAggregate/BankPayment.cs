using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

/// <summary>The Plaid transfer that paid an order.</summary>
public class BankPayment : BaseEntity
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string CheckoutId { get; set; } = string.Empty;
    public string TransferId { get; set; } = string.Empty;
    public string Speed { get; set; } = string.Empty;
    public decimal ItemsTotal { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal Fee { get; set; }
    /// <summary>Everything the shopper was charged: items + shipping + fee.</summary>
    public decimal Amount { get; set; }
    public DateTime? ArrivesOn { get; set; }
    public string Status { get; set; } = BankPaymentStatus.Paid;
    public DateTimeOffset? ShippedOn { get; set; }
    public DateTimeOffset? CancelledOn { get; set; }
    public string? RefundId { get; set; }
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
}

public static class BankPaymentStatus
{
    public const string Paid = "paid";
    public const string Cancelled = "cancelled";
}
