using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

public enum BankPaymentStatus
{
    Paid = 0,
    Cancelled = 1
}

public enum BankPaymentTransferState
{
    /// <summary>The money is (or will be) taken from the shopper.</summary>
    Charged = 0,
    /// <summary>The transfer was cancelled before any money moved.</summary>
    Cancelled = 1,
    /// <summary>The money was refunded to the shopper.</summary>
    Refunded = 2
}

/// <summary>
/// The charge behind an order paid by bank. A large order is charged as several Plaid transfers,
/// because a single transfer is limited by the payment network.
/// </summary>
public class BankPayment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public Guid CheckoutId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserKey { get; set; } = string.Empty;
    public string Speed { get; set; } = string.Empty;
    public decimal OrderTotal { get; set; }
    public decimal Fee { get; set; }
    public decimal TotalCharged { get; set; }
    public DateTime ArrivesOn { get; set; }
    public BankPaymentStatus Status { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<BankPaymentTransfer> Transfers { get; set; } = new();
}

public class BankPaymentTransfer : BaseEntity
{
    public int BankPaymentId { get; set; }
    public string PlaidTransferId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Network { get; set; } = string.Empty;
    public BankPaymentTransferState State { get; set; }
}
