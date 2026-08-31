using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record BillingAddressInput(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? State,
    string PostalCode,
    string CountryCode);

public sealed record CardInput(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    BillingAddressInput BillingAddress);

public sealed record OrderLineInput(int CatalogItemId, int Quantity);

public sealed record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

public sealed record PlaceOrderRequest(IReadOnlyList<OrderLineInput> Items, ShippingAddressInput ShippingAddress);
public sealed record PlaceOrderResponse(int OrderId, string Status, decimal Total, string Currency);

public sealed record PayOrderRequest(CardInput? Card, int? PaymentMethodId);

public sealed record RefundOrderRequest(decimal? Amount, string IdempotencyKey);

public sealed record SavePaymentMethodRequest(CardInput Card);

public sealed record PaymentMethodResponse(
    int PaymentMethodId,
    string? Brand,
    string? CardType,
    string LastDigits,
    string? Expiry);

public sealed record RefundResponse(int RefundId, string Status, decimal Amount, string Currency);

public sealed record PaymentStateResponse(
    string Status,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetProceeds,
    decimal RefundedAmount,
    IReadOnlyList<RefundResponse> Refunds);

public sealed record OrderResponse(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    decimal Total,
    string? Currency,
    PaymentStateResponse Payment);

public sealed record ProviderTransactionResponse(
    string? TransactionId,
    string? PayPalReferenceId,
    string? EventCode,
    DateTimeOffset? InitiatedAt,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? InvoiceId,
    string? CustomId,
    int? OrderId,
    string MatchStatus);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset? PayPalLastRefreshedAt,
    IReadOnlyList<ProviderTransactionResponse> Transactions,
    IReadOnlyList<int> EShopOnlyOrderIds);

public sealed record AuthorizationResult(
    string OrderId,
    string OrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    decimal Amount,
    string Currency,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record CaptureResult(
    string CaptureId,
    string Status,
    decimal Gross,
    string Currency,
    decimal? Fee,
    decimal? Net,
    DateTimeOffset? CreatedAt);

public sealed record RefundProviderResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? UpdatedAt);

public sealed record SavedCardProviderResult(
    string PaymentTokenId,
    string CustomerId,
    string? Brand,
    string? CardType,
    string LastDigits,
    string? Expiry);

public sealed record ProviderTransaction(
    string? TransactionId,
    string? PayPalReferenceId,
    string? EventCode,
    DateTimeOffset? InitiatedAt,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? InvoiceId,
    string? CustomId);

public sealed record TransactionSearchResult(IReadOnlyList<ProviderTransaction> Transactions, DateTimeOffset? LastRefreshedAt);
