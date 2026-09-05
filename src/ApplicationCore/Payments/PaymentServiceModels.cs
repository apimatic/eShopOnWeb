using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>One line of a placed-order request: which catalog item and how many.</summary>
public class OrderItemInput
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Shipping address for a placed order.</summary>
public class AddressInput
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PlaceOrderInput
{
    public IReadOnlyList<OrderItemInput> Items { get; set; } = Array.Empty<OrderItemInput>();
    public AddressInput ShipToAddress { get; set; } = new AddressInput();
}

public class PlaceOrderResult
{
    public bool Succeeded { get; init; }
    public PaymentError? Error { get; init; }
    public int OrderId { get; init; }
    public decimal Total { get; init; }
}

public class PayOrderResult
{
    public bool Succeeded { get; init; }
    public PaymentError? Error { get; init; }
    public Payment? Payment { get; init; }
}

public class OperatorActionResult
{
    public bool Succeeded { get; init; }
    public PaymentError? Error { get; init; }
    public Payment? Payment { get; init; }
}

public class RefundAction
{
    public bool Succeeded { get; init; }
    public PaymentError? Error { get; init; }
    public PaymentRefund? Refund { get; init; }
    public Payment? Payment { get; init; }
}

public class CardActionResult
{
    public bool Succeeded { get; init; }
    public PaymentError? Error { get; init; }
    public SavedCard? Card { get; init; }
}

public class MyOrderItemView
{
    public int CatalogItemId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? PictureUri { get; init; }
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
}

public class PaymentRefundView
{
    public int RefundId { get; init; }
    public string? PayPalRefundId { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
}

public class PaymentView
{
    public int PaymentId { get; init; }
    public string State { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public DateTimeOffset? AuthorizationExpiresAt { get; init; }
    public string? CaptureId { get; init; }
    public string? CaptureStatus { get; init; }
    public decimal? CapturedAmount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public DateTimeOffset? CapturedAt { get; init; }
    public decimal RefundedAmount { get; init; }
    public IReadOnlyList<PaymentRefundView> Refunds { get; init; } = Array.Empty<PaymentRefundView>();
}

public class MyOrderView
{
    public int OrderId { get; init; }
    public DateTimeOffset OrderDate { get; init; }
    public string Status { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public string Currency { get; init; } = string.Empty;
    public IReadOnlyList<MyOrderItemView> Items { get; init; } = Array.Empty<MyOrderItemView>();
    public PaymentView? Payment { get; init; }
}

/// <summary>One eShop payment event (capture or refund) lined up against PayPal's records.</summary>
public class ShopPaymentRecord
{
    public int OrderId { get; init; }

    /// <summary>The unique key of the payment this event belongs to.</summary>
    public string PaymentKey { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;
    public string PayPalId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public bool Matched { get; set; }
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public IReadOnlyList<ReconciliationTransaction> PayPalTransactions { get; init; } =
        Array.Empty<ReconciliationTransaction>();

    public IReadOnlyList<ShopPaymentRecord> ShopPayments { get; init; } = Array.Empty<ShopPaymentRecord>();
    public IReadOnlyList<ReconciliationTransaction> PayPalOnly { get; init; } =
        Array.Empty<ReconciliationTransaction>();

    public IReadOnlyList<ShopPaymentRecord> ShopOnly { get; init; } = Array.Empty<ShopPaymentRecord>();
    public int MatchedCount { get; init; }

    /// <summary>When PayPal last refreshed its transaction reporting, if reported.</summary>
    public string? LastRefreshedDatetime { get; init; }
}
