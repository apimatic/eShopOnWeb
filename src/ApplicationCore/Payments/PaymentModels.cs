using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public class PaymentCard
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public FinixAddress? Address { get; set; }
}

public class OrderWithPayment
{
    public OrderWithPayment(Order order, Payment? payment)
    {
        Order = order;
        Payment = payment;
    }

    public Order Order { get; }
    public Payment? Payment { get; }
}

public class ReconciliationEntry
{
    public string FinixId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? State { get; set; }
    public long AmountCents { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public int? OrderId { get; set; }
    public bool Matched { get; set; }
    public string? MatchType { get; set; }
}

public class EshopPaymentEntry
{
    public int OrderId { get; set; }
    public int? PaymentId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureTransferId { get; set; }
    public long? AmountCents { get; set; }
    public long? CapturedAmountCents { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? AuthorizedAt { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntry> FinixTransactions { get; set; } = new();
    public List<ReconciliationEntry> UnmatchedFinixTransactions { get; set; } = new();
    public List<EshopPaymentEntry> EshopOrdersWithoutFinixRecord { get; set; } = new();
}

public class SavedCardDetails
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public FinixAddress? Address { get; set; }
}

public class PaymentMethodResult
{
    public PaymentMethodResult(SavedPaymentMethod savedPaymentMethod)
    {
        SavedPaymentMethod = savedPaymentMethod;
    }

    public SavedPaymentMethod SavedPaymentMethod { get; }
}

public class RefundResult
{
    public RefundResult(Payment payment, Refund refund)
    {
        Payment = payment;
        Refund = refund;
    }

    public Payment Payment { get; }
    public Refund Refund { get; }
}
