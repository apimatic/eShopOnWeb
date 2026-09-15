namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record PlaceOrderRequest(
    IReadOnlyList<PlaceOrderItemRequest> Items,
    ShippingAddressRequest ShippingAddress);

public sealed record PlaceOrderItemRequest(int CatalogItemId, int Quantity);

public sealed record ShippingAddressRequest(
    string Street,
    string City,
    string State,
    string Country,
    string ZipCode);

public sealed record PlaceOrderResponse(int OrderId, string PaymentState, decimal Total);

public sealed record PayOrderRequest(CardInput? Card, int? PaymentMethodId);

public sealed record RefundOrderRequest(decimal? Amount, string IdempotencyKey);

public sealed record RefundOrderResponse(
    int RefundId,
    string PayPalRefundId,
    string Status,
    decimal Amount,
    string Currency,
    decimal RemainingRefundableAmount);

public sealed record SavePaymentMethodRequest(CardInput Card);

public sealed record SavePaymentMethodResponse(
    int PaymentMethodId,
    string Brand,
    string Last4,
    string? Expiry);

public sealed record PaymentMethodResponse(
    int PaymentMethodId,
    string Brand,
    string Last4,
    string? Expiry,
    DateTimeOffset CreatedAt);

public sealed record OrderItemResponse(
    int CatalogItemId,
    string ProductName,
    decimal UnitPrice,
    int Quantity);

public sealed record RefundResponse(
    int RefundId,
    string PayPalRefundId,
    string Status,
    decimal Amount,
    DateTimeOffset CreatedAt);

public sealed record OrderPaymentResponse(
    string State,
    string? Currency,
    string? PayPalOrderId,
    string? PayPalOrderStatus,
    string? AuthorizationId,
    string? AuthorizationStatus,
    decimal? AuthorizedAmount,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetProceeds,
    decimal RefundedAmount,
    decimal? RemainingRefundableAmount,
    IReadOnlyList<RefundResponse> Refunds);

public sealed record OrderResponse(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<OrderItemResponse> Items,
    OrderPaymentResponse Payment);

public sealed record ReconciliationTransactionResponse(
    string PayPalTransactionId,
    int? OrderId,
    string MatchState,
    string? ReferenceId,
    string? ReferenceIdType,
    string? EventCode,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? PayPalStatus,
    string? InvoiceId);

public sealed record ReconciliationMissingOrderResponse(
    int OrderId,
    string PaymentState,
    string PayPalResourceType,
    string PayPalResourceId,
    DateTimeOffset OccurredAt,
    decimal Amount,
    string Currency);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationTransactionResponse> Transactions,
    IReadOnlyList<ReconciliationMissingOrderResponse> EshopRecordsMissingFromPayPal);
