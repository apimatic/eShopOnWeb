using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record PlaceOrderLine(int CatalogItemId, int Quantity);

public enum PlaceOrderStatus
{
    Created,
    Invalid
}

public sealed record PlaceOrderResult(PlaceOrderStatus Status, int? OrderId, decimal? Total, string? Currency, string? Message);

public enum PayOrderStatus
{
    /// <summary>The order total was charged by this call (or by the unknown-outcome call it settled).</summary>
    Paid,

    /// <summary>The order was already paid; nothing was charged again.</summary>
    AlreadyPaid,

    /// <summary>The card was declined; the order stays unpaid.</summary>
    Refused,

    /// <summary>The provider holds the payment without a final result yet.</summary>
    Pending,

    /// <summary>The issuer requires shopper interaction this API cannot perform; the order stays unpaid.</summary>
    ActionRequired,

    /// <summary>The provider rejected the card details as invalid; the order stays unpaid.</summary>
    Rejected,

    /// <summary>Another payment for this order is in progress.</summary>
    InProgress,

    NotFound,

    /// <summary>The order cannot be charged (nothing to pay, or its total is not representable in the currency).</summary>
    NotPayable,

    /// <summary>The request itself is incomplete.</summary>
    Invalid,

    /// <summary>The provider refused the shop (configuration or credentials); nothing was charged.</summary>
    ProviderUnavailable,

    /// <summary>The provider did not answer in time; the outcome is recorded as unknown and settled by the next pay call.</summary>
    ProviderDidNotRespond
}

public sealed record PayOrderResult(
    PayOrderStatus Status,
    int OrderId,
    string Message,
    OrderPaymentStatus? PaymentStatus = null,
    string? PspReference = null,
    decimal? Amount = null,
    string? Currency = null,
    string? RefusalReason = null);

public enum RefundOrderStatus
{
    /// <summary>The provider accepted the refund request.</summary>
    Accepted,

    /// <summary>A refund with the same client request key already exists; it is returned instead of a new one.</summary>
    Replayed,

    NotFound,

    /// <summary>The order has no payment to refund.</summary>
    NotRefundable,

    /// <summary>The amount exceeds what can still be refunded.</summary>
    ExceedsRefundable,

    Invalid,

    /// <summary>Another refund on this order is in progress.</summary>
    InProgress,

    /// <summary>The provider rejected the refund request.</summary>
    Rejected,

    ProviderUnavailable,

    /// <summary>The provider did not answer in time; the refund is recorded as unknown (amount reserved) and settled on the next refund call.</summary>
    ProviderDidNotRespond
}

public sealed record RefundOrderResult(
    RefundOrderStatus Status,
    int OrderId,
    string Message,
    Guid? RefundId = null,
    RefundStatus? RefundStatus = null,
    decimal? Amount = null,
    string? Currency = null,
    decimal? RemainingRefundable = null,
    string? PspReference = null);
