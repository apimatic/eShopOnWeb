using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

/// <summary>The Plaid money movement behind an order paid by bank.</summary>
public class BankPayment : BaseEntity
{
    public int OrderId { get; set; }
    public Guid CheckoutId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Speed { get; set; } = string.Empty;
    public decimal Fee { get; set; }
    /// <summary>Everything the shopper was charged: order total plus fee.</summary>
    public decimal Charged { get; set; }
    public DateTime? ArrivesOn { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<BankPaymentTransfer> Transfers { get; set; } = new();
}

public class BankPaymentTransfer : BaseEntity
{
    public int BankPaymentId { get; set; }
    public string PlaidTransferId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Network { get; set; } = string.Empty;
    /// <summary>Set once the transfer was cancelled or fully refunded.</summary>
    public DateTimeOffset? ReversedAt { get; set; }
}
