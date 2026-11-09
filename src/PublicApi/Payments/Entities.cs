using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Links a shopper (by e-mail) to the customer record kept at Stax.</summary>
public class ShopperAccount
{
    public string Email { get; set; } = "";
    public string? StaxCustomerId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>A card or bank account a shopper saved through the shop.</summary>
public class SavedPaymentMethod
{
    public string Id { get; set; } = "";               // the Stax payment method id
    public string ShopperEmail { get; set; } = "";
    public string Type { get; set; } = "";             // card | bank
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int? ExpMonth { get; set; }
    public int? ExpYear { get; set; }
    public string? BankType { get; set; }
    public string? HolderType { get; set; }
    public string? Nickname { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RemovedAt { get; set; }
}

public static class PaymentStatuses
{
    public const string Unpaid = "unpaid";
    public const string Processing = "processing";   // bank payment accepted, waiting for the bank to settle it
    public const string Paid = "paid";
    public const string Failed = "failed";
    public const string Voided = "voided";            // payment cancelled before it settled
}

public static class RefundStatuses
{
    public const string None = "none";
    public const string Pending = "pending";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public class ShopOrder
{
    public Guid Id { get; set; }
    public string BuyerEmail { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public decimal Subtotal { get; set; }
    public List<ShopOrderLine> Lines { get; set; } = new();

    public string PaymentStatus { get; set; } = PaymentStatuses.Unpaid;
    public string? PaymentMethodId { get; set; }
    public string? PaymentMethodType { get; set; }
    public decimal? Fee { get; set; }
    public decimal? Total { get; set; }
    public string? StaxTransactionId { get; set; }
    public string? PaymentMessage { get; set; }
    public DateTime? PaidAt { get; set; }

    public string RefundStatus { get; set; } = RefundStatuses.None;
    public string? RefundTransactionId { get; set; }
    public decimal? RefundAmount { get; set; }
    public string? RefundMessage { get; set; }
    public DateTime? RefundRequestedAt { get; set; }
    public DateTime? RefundCompletedAt { get; set; }
    public DateTime? RefundLastAttemptAt { get; set; }

    public bool IsPaid => PaymentStatus == PaymentStatuses.Paid;
}

public class ShopOrderLine
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }
    public int CatalogItemId { get; set; }
    public string Name { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}
