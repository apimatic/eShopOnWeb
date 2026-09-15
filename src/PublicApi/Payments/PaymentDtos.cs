using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);
public sealed record PlaceOrderRequest(IReadOnlyList<OrderLineRequest> Items, ShippingAddressRequest ShippingAddress);
public sealed record PayOrderRequest(CardDetails? Card, int? PaymentMethodId);
public sealed record RefundOrderRequest(decimal? Amount, string IdempotencyKey);
public sealed record SavePaymentMethodRequest(CardDetails Card);

public sealed record OrderLineResponse(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);
public sealed record RefundResponse(int RefundId, string PayPalRefundId, string Status, decimal Amount,
    string Currency, string IdempotencyKey, DateTimeOffset CreatedAt);
public sealed record PaymentStateResponse(string Status, string? PayPalOrderId, string? AuthorizationId,
    string? AuthorizationStatus, DateTimeOffset? AuthorizationExpiresAt, string? CaptureId,
    string? CaptureStatus, decimal? CapturedAmount, decimal? PayPalFee, decimal? NetProceeds,
    decimal RefundedAmount, decimal RefundableAmount, string? Currency,
    IReadOnlyList<RefundResponse> Refunds);
public sealed record OrderResponse(int OrderId, DateTimeOffset OrderDate, decimal Total,
    ShippingAddressRequest ShippingAddress, IReadOnlyList<OrderLineResponse> Items,
    PaymentStateResponse Payment);
public sealed record CreatedOrderResponse(int OrderId, OrderResponse Order);
public sealed record PaymentMethodResponse(int PaymentMethodId, string Brand, string Last4,
    string? Expiry, DateTimeOffset CreatedAt);
public sealed record CreatedPaymentMethodResponse(int PaymentMethodId, PaymentMethodResponse PaymentMethod);
public sealed record CreatedRefundResponse(int RefundId, RefundResponse Refund, OrderResponse Order);

public sealed record ReconciliationItem(
    string Source,
    string MatchStatus,
    int? OrderId,
    string? PayPalTransactionId,
    string? PayPalReferenceId,
    string? EventCode,
    string? TransactionStatus,
    decimal? Amount,
    decimal? Fee,
    string? Currency,
    DateTimeOffset? InitiatedAt,
    string? InvoiceId);

public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To,
    int PayPalTransactionCount, int MatchedCount, int PayPalOnlyCount, int EShopOnlyCount,
    IReadOnlyList<ReconciliationItem> Items);
