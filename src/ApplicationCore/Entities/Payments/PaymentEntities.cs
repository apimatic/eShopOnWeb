using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Payments;

/// <summary>Links a shopper (identified by the user name / e-mail in the JWT) to the shopper's Stax customer.</summary>
public class PaymentCustomer
{
    public int Id { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string StaxCustomerId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A card or bank account a shopper saved. Removal is a soft delete so support can still see it.
/// </summary>
public class SavedPaymentMethod
{
    public int Id { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string StaxPaymentMethodId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty; // "card" | "bank"
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Nickname { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RemovedAt { get; set; }
}

public static class PaymentStatuses
{
    public const string Processing = "processing";
    public const string Pending = "pending";   // bank payment submitted, not settled yet
    public const string Paid = "paid";
    public const string Failed = "failed";
}

public static class RefundStatuses
{
    public const string None = "none";
    public const string Pending = "pending";
    public const string Refunded = "refunded";
    public const string Failed = "failed";
}

/// <summary>The payment attempt of an order, plus the state of its refund.</summary>
public class OrderPayment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string StaxPaymentMethodId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = PaymentStatuses.Processing;
    public string? StaxTransactionId { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PaidAt { get; set; }

    public string RefundStatus { get; set; } = RefundStatuses.None;
    public decimal? RefundAmount { get; set; }
    public string? RefundRequestedBy { get; set; }
    public DateTimeOffset? RefundRequestedAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public string? RefundMessage { get; set; }
    public int RefundAttempts { get; set; }
}

public class InvoiceScheduleRecord
{
    public int Id { get; set; }
    public string StaxScheduleId { get; set; } = string.Empty;
    public string StaxCustomerId { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime FirstInvoiceDate { get; set; }
    public int DayOfMonth { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class SellerRecord
{
    public int Id { get; set; }
    public string StaxMerchantId { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
