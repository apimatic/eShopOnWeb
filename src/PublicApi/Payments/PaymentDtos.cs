using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
public sealed record PlaceOrderRequest(IReadOnlyList<OrderLineRequest> Items);
public sealed record PlaceOrderResponse(int OrderId, decimal Total, string Currency, string PaymentStatus);

public sealed record PayOrderRequest(CardRequestDto? Card, int? PaymentMethodId);
public sealed record CardRequestDto(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    BillingAddressRequestDto? BillingAddress);
public sealed record BillingAddressRequestDto(
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? Region,
    string? PostalCode);

public sealed record RefundOrderRequest(decimal? Amount);
public sealed record RefundResponse(int RefundId, string? PayPalRefundId, decimal Amount, string Currency, string Status);

public sealed record PaymentStateResponse(
    int OrderId,
    decimal Total,
    string Currency,
    string PaymentStatus,
    string FulfilmentStatus,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetProceeds,
    decimal RefundedAmount,
    IReadOnlyList<RefundResponse> Refunds);

public sealed record SavePaymentMethodRequest(CardRequestDto Card);
public sealed record PaymentMethodResponse(
    int PaymentMethodId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? Type,
    string? CardholderName);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ReconciliationItem> Items);

public sealed record ReconciliationItem(
    string MatchStatus,
    int? OrderId,
    string? PayPalTransactionId,
    string? PayPalReferenceId,
    string? TransactionStatus,
    string? EventCode,
    DateTimeOffset? TransactionDate,
    decimal? Amount,
    string? Currency,
    string? EShopPaymentStatus,
    string? EShopCaptureId);
