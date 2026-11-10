using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

public enum BankPaymentStatus
{
    Paid = 0,
    Cancelled = 1
}

public class BankPaymentTransfer
{
    public int Id { get; set; }
    public string TransferId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

/// <summary>The Plaid transfer(s) that paid an order.</summary>
public class BankPayment : BaseEntity
{
    public int OrderId { get; set; }
    public Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order? Order { get; set; }
    public Guid CheckoutId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public int ConnectionId { get; set; }
    /// <summary>The Plaid transfers that carried the payment (one, unless it exceeded one transfer's limit).</summary>
    public System.Collections.Generic.List<BankPaymentTransfer> Transfers { get; set; } = new();
    public string Speed { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public decimal Fee { get; set; }
    public string ArrivesOn { get; set; } = string.Empty;
    public BankPaymentStatus Status { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public DateTimeOffset? ShippedOn { get; set; }
    public DateTimeOffset? CancelledOn { get; set; }
    public string? RefundId { get; set; }

    /// <summary>Everything the shopper was charged: order total plus fee.</summary>
    public decimal Charged => Total + Fee;
}
