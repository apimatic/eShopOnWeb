using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record CreateOrderItem(int CatalogItemId, int Quantity);

public sealed record ShippingAddressDetails(string Street, string City, string State,
    string Country, string ZipCode);

public sealed record CreateOrderCommand(IReadOnlyList<CreateOrderItem> Items,
    ShippingAddressDetails? ShippingAddress);

public sealed record CardDetails(string Number, int ExpirationMonth, int ExpirationYear,
    string SecurityCode, string Name, string? AddressLine1 = null, string? City = null,
    string? Region = null, string? PostalCode = null, string? Country = null);

public sealed record PayOrderCommand(int? PaymentMethodId, CardDetails? Card,
    string? IdempotencyKey);

public sealed record RefundCommand(long? AmountInCents, string IdempotencyKey);

public sealed record SaveCardCommand(CardDetails Card, string? Alias);

public sealed record OrderItemResponse(int CatalogItemId, string ProductName, decimal UnitPrice,
    int Quantity);

public sealed record PaymentResponse(int PaymentId, int OrderId, string Status, string Currency,
    long AmountInCents, decimal Amount, string? FinixAuthorizationId, string? AuthorizationState,
    DateTimeOffset? AuthorizedAt, string? FinixCaptureId, string? CaptureState,
    long? CapturedAmountInCents, decimal? CapturedAmount, long? FinixFeeInCents, decimal? FinixFee,
    long? NetAmountInCents, decimal? NetAmount, long RefundedAmountInCents, decimal RefundedAmount,
    IReadOnlyList<RefundResponse> Refunds, string? FailureCode, string? FailureMessage);

public sealed record RefundResponse(int RefundId, long AmountInCents, decimal Amount,
    string IdempotencyKey, string? FinixRefundId, string? State, DateTimeOffset CreatedAt);

public sealed record OrderResponse(int OrderId, string BuyerId, DateTimeOffset OrderDate,
    decimal Total, long TotalInCents, string Currency, string Status,
    IReadOnlyList<OrderItemResponse> Items, PaymentResponse? Payment);

public sealed record PaymentMethodResponse(int PaymentMethodId, string? Brand, string? Last4,
    int? ExpirationMonth, int? ExpirationYear, string? CardType, string? Alias,
    DateTimeOffset CreatedAt);

public sealed record RefundOperationResponse(int RefundId, int OrderId, RefundResponse Refund,
    PaymentResponse Payment);

public sealed record ReconciliationTransaction(string FinixId, string Kind, string? Type,
    string? State, long? AmountInCents, string? Currency, DateTimeOffset? CreatedAt, int? OrderId,
    bool Matched, string? Issue);

public sealed record ReconciliationOrder(int OrderId, string BuyerId, DateTimeOffset OrderDate,
    decimal Total, long TotalInCents, string PaymentStatus, string? FinixAuthorizationId,
    string? FinixCaptureId, long RefundedAmountInCents, bool Matched, string? Issue);

public sealed record ReconciliationReport(DateTimeOffset From, DateTimeOffset To, string Currency,
    IReadOnlyList<ReconciliationTransaction> FinixTransactions,
    IReadOnlyList<ReconciliationOrder> Orders,
    IReadOnlyList<ReconciliationTransaction> UnmatchedFinixTransactions,
    IReadOnlyList<ReconciliationOrder> UnmatchedOrders);
