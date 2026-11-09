using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>Maps a shopper (by email) to the customer record kept at Stax.</summary>
public class ShopperAccount
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string StaxCustomerId { get; set; } = "";
}

/// <summary>Tombstone for a closed account. Only a hash of the email is kept, never the email itself.</summary>
public class ClosedShopper
{
    public int Id { get; set; }
    public string EmailHash { get; set; } = "";
    public DateTimeOffset ClosedAt { get; set; }
}

public class SavedPaymentMethod
{
    /// <summary>The Stax payment method id.</summary>
    public string Id { get; set; } = "";
    public string ShopperEmail { get; set; } = "";
    public string Method { get; set; } = "";   // card | bank
    public string Nickname { get; set; } = "";
    public string? Brand { get; set; }
    public string? BankName { get; set; }
    public string Last4 { get; set; } = "";
    public string? CardExp { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
}

public static class OrderStatuses
{
    public const string Created = "created";
    public const string Processing = "processing";
    public const string PendingSettlement = "pending_settlement";
    public const string Paid = "paid";
    public const string Refunded = "refunded";
}

public static class RefundStatuses
{
    public const string None = "none";
    public const string Pending = "pending";
    public const string Refunded = "refunded";
    public const string Failed = "failed";
}

public class ShopOrder
{
    public int Id { get; set; }
    public string ShopperEmail { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = OrderStatuses.Created;
    public string? PaymentMethodId { get; set; }
    public string? PaymentMethodKind { get; set; }
    public string? StaxTransactionId { get; set; }
    public int PaymentAttempts { get; set; }
    public string? PaymentIdempotencyKey { get; set; }
    public DateTimeOffset? ProcessingStartedAt { get; set; }
    public string? LastPaymentError { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string RefundStatus { get; set; } = RefundStatuses.None;
    public DateTimeOffset? RefundRequestedAt { get; set; }
    public string? RefundTransactionId { get; set; }
    public string? RefundMessage { get; set; }
    public int RefundAttempts { get; set; }
    public DateTimeOffset? RefundLastAttemptAt { get; set; }
    public List<ShopOrderItem> Items { get; set; } = new();
}

public class ShopOrderItem
{
    public int Id { get; set; }
    public int ShopOrderId { get; set; }
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class InvoiceScheduleRecord
{
    /// <summary>The Stax schedule id.</summary>
    public string Id { get; set; } = "";
    public string CustomerEmail { get; set; } = "";
    public string Company { get; set; } = "";
    public string StaxCustomerId { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime FirstInvoiceDate { get; set; }
    public int DayOfMonth { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

public class SellerRecord
{
    public int Id { get; set; }
    public string StaxMerchantId { get; set; } = "";
    public string Company { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
