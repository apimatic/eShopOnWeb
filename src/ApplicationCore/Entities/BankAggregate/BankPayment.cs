using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

public enum BankPaymentStatus
{
    Paid = 0,
    Cancelling = 1,
    Cancelled = 2
}

/// <summary>The money collected by bank transfer for an order.</summary>
public class BankPayment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public Guid CheckoutId { get; set; }
    public string Speed { get; set; } = "standard";
    /// <summary>Amount of the order itself (items and shipping).</summary>
    public decimal Total { get; set; }
    /// <summary>Express fee charged on top of the total.</summary>
    public decimal Fee { get; set; }
    public DateTime ArrivesOn { get; set; }
    public BankPaymentStatus Status { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int Version { get; set; }
    public List<BankPaymentTransfer> Transfers { get; set; } = new();
}

public class BankPaymentTransfer
{
    public int Id { get; set; }
    public int BankPaymentId { get; set; }
    public string PlaidTransferId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
