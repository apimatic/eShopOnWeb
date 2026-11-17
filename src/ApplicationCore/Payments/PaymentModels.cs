using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed class CardBillingAddress
{
    public string? Line1 { get; init; }
    public string? Line2 { get; init; }
    public string? City { get; init; }
    public string? Region { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
}

public sealed class CardDetails
{
    public string Number { get; init; } = string.Empty;
    public string? Expiration { get; init; }
    public int? ExpirationMonth { get; init; }
    public int? ExpirationYear { get; init; }
    public string? SecurityCode { get; init; }
    public string Name { get; init; } = string.Empty;
    public CardBillingAddress? BillingAddress { get; init; }
}

public sealed class OrderLineInput
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public sealed class CreateOrderInput
{
    public IReadOnlyList<OrderLineInput> Items { get; init; } = Array.Empty<OrderLineInput>();
    public Address? ShippingAddress { get; init; }
}

public sealed class RefundResult
{
    public RefundResult(PaymentRefund refund, OrderPayment payment)
    {
        Refund = refund;
        Payment = payment;
    }

    public PaymentRefund Refund { get; }
    public OrderPayment Payment { get; }
}

public sealed class ReconciliationFinixTransaction
{
    public string Id { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string? Type { get; init; }
    public string? State { get; init; }
    public long AmountCents { get; init; }
    public long? FeeCents { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string? ParentTransferId { get; init; }
    public int? OrderId { get; init; }
    public bool Matched { get; set; }
}

public sealed class ReconciliationOrder
{
    public int OrderId { get; init; }
    public DateTimeOffset OrderDate { get; init; }
    public long AmountCents { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public string? FinixAuthorizationId { get; init; }
    public string? FinixCaptureTransferId { get; init; }
    public IReadOnlyList<string> FinixRefundTransferIds { get; init; } = Array.Empty<string>();
    public bool Matched { get; set; }
}

public sealed class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public IReadOnlyList<ReconciliationFinixTransaction> FinixTransactions { get; init; }
        = Array.Empty<ReconciliationFinixTransaction>();
    public IReadOnlyList<ReconciliationOrder> EShopOrders { get; init; }
        = Array.Empty<ReconciliationOrder>();
    public IReadOnlyList<string> UnmatchedFinixTransactionIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<int> UnmatchedOrderIds { get; init; } = Array.Empty<int>();
}
