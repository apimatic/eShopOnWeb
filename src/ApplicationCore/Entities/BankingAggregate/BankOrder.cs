using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;

public static class BankOrderStatus
{
    public const string Paid = "paid";
    public const string Cancelled = "cancelled";
}

/// <summary>An order paid by bank through a Plaid transfer.</summary>
public class BankOrder : BaseEntity
{
    public string OwnerId { get; set; } = string.Empty;
    public string CheckoutId { get; set; } = string.Empty;
    public int ConnectionId { get; set; }
    public string ItemsJson { get; set; } = "[]";
    public decimal Subtotal { get; set; }
    public decimal Shipping { get; set; }
    public decimal Fee { get; set; }

    /// <summary>Subtotal + shipping + fee: what the bank was charged.</summary>
    public decimal Charged { get; set; }
    public string Speed { get; set; } = "standard";
    public string TransferId { get; set; } = string.Empty;
    public DateOnly? ArrivesOn { get; set; }
    public string Status { get; set; } = BankOrderStatus.Paid;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ShippedOn { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? RefundId { get; set; }
}
