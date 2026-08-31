using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record ApiAddress(
    string Street,
    string City,
    string State,
    string Country,
    string PostalCode,
    string CountryCode);

public sealed record CardInput(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    ApiAddress BillingAddress);

public sealed record CreateOrderLine(int CatalogItemId, int Quantity);
public sealed record CreateOrderRequest(IReadOnlyList<CreateOrderLine> Items, ApiAddress ShippingAddress);
public sealed record CreateOrderResponse(int OrderId, string PaymentStatus, decimal Total, string Currency);
public sealed record PayOrderRequest(CardInput? Card, int? PaymentMethodId);
public sealed record RefundOrderRequest(string IdempotencyKey, decimal? Amount);
public sealed record SavePaymentMethodRequest(CardInput Card);

public sealed record PaymentMethodResponse(
    int PaymentMethodId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

public sealed record PaymentRefundResponse(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency,
    string IdempotencyKey);

public sealed record OrderPaymentResponse(
    int OrderId,
    string PaymentStatus,
    decimal Total,
    string Currency,
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
    IReadOnlyList<PaymentRefundResponse> Refunds);

public sealed record MyOrderLineResponse(
    int CatalogItemId, string ProductName, int Quantity, decimal UnitPrice);

public sealed record MyOrderResponse(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Currency,
    string PaymentStatus,
    IReadOnlyList<MyOrderLineResponse> Items,
    OrderPaymentResponse Payment);

public sealed record ReconciliationEntry(
    string MatchStatus,
    int? OrderId,
    string? PayPalTransactionId,
    string? PayPalReferenceId,
    string? TransactionStatus,
    string? TransactionEventCode,
    DateTimeOffset? TransactionTime,
    decimal? Amount,
    decimal? Fee,
    string? Currency,
    string? Note);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Entries);

public sealed record ProviderCardSource(
    string? VaultId,
    CardInput? Card);

public sealed record ProviderAuthorization(
    string PayPalOrderId,
    string? PayPalOrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpiresAt);

public sealed record ProviderAuthorizationStatus(
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpiresAt);

public sealed record ProviderCapture(
    string CaptureId,
    string Status,
    decimal Amount,
    string Currency,
    decimal? PayPalFee,
    decimal? NetAmount);

public sealed record ProviderRefund(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public sealed record ProviderPaymentMethod(
    string TokenId,
    string CustomerId,
    string? CardholderName,
    string? Brand,
    string? LastDigits,
    string? Expiry);

public sealed record ProviderTransaction(
    string? TransactionId,
    string? ReferenceId,
    string? EventCode,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt,
    decimal? Amount,
    decimal? Fee,
    string? Currency,
    string? Status,
    string? InvoiceId,
    string? CustomField);
