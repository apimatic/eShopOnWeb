using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

public static class BankOrderStatus
{
    public const string Paid = "paid";
    public const string Cancelled = "cancelled";
}

public static class BankPaymentSpeeds
{
    public const string Standard = "standard";
    public const string Express = "express";
}

/// <summary>The bank payment behind an order (the order id is the id of the shop's Order).</summary>
public class BankOrder
{
    /// <summary>Same as the id of the Order.</summary>
    public int OrderId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid CheckoutId { get; set; }
    public string Speed { get; set; } = BankPaymentSpeeds.Standard;
    public string TransferId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    /// <summary>Order total (items and shipping), without fee.</summary>
    public decimal Total { get; set; }
    public decimal Fee { get; set; }
    /// <summary>What the bank was actually charged: Total + Fee.</summary>
    public decimal Charged { get; set; }
    public DateTime? ArrivesOn { get; set; }
    public string Status { get; set; } = BankOrderStatus.Paid;
    /// <summary>Set once the order has shipped; a shipped order can no longer be cancelled.</summary>
    public DateTime? ShippedOnUtc { get; set; }
    public string? RefundId { get; set; }
    public DateTime? CancelledOnUtc { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
