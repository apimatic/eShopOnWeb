using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed record BillingAddressInput(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

public sealed record CardInput(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    BillingAddressInput BillingAddress);

public sealed record OrderLineInput(int CatalogItemId, int Quantity);
public sealed record PlaceOrderRequest(IReadOnlyList<OrderLineInput> Items);
public sealed record PlaceOrderResponse(int OrderId, string Status, decimal Total, string Currency);
public sealed record PayOrderRequest(CardInput? Card, int? PaymentMethodId);
public sealed record RefundOrderRequest(decimal? Amount, string IdempotencyKey);
public sealed record SavePaymentMethodRequest(CardInput Card);

public sealed record PaymentStateResponse(
    int OrderId,
    string OrderStatus,
    string PaymentState,
    decimal Total,
    string Currency,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? MerchantNet,
    decimal RefundedAmount,
    string? LastProviderError);

public sealed record RefundResponse(int RefundId, string Status, decimal Amount);
public sealed record PaymentMethodResponse(
    int PaymentMethodId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardType,
    string? VerificationStatus);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationLine> Lines);

public sealed record ReconciliationLine(
    string Classification,
    string? PayPalTransactionId,
    int? OrderId,
    string? TransactionStatus,
    decimal? Amount,
    decimal? Fee,
    string? Currency,
    DateTimeOffset? TransactionTime);

public sealed record ProviderCard(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    BillingAddressInput BillingAddress,
    string? VaultId = null);

public sealed record AuthorizationResult(
    string PayPalOrderId,
    string? OrderStatus,
    bool PayerActionRequired,
    string? AuthorizationId,
    string? AuthorizationStatus,
    decimal? Amount,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record ProviderAuthorization(
    string Id, string? Status, decimal Amount, DateTimeOffset? CreatedAt, DateTimeOffset? ExpiresAt);
public sealed record ProviderCapture(
    string Id, string? Status, decimal Amount, decimal? Fee, decimal? Net, DateTimeOffset? CreatedAt);
public sealed record ProviderRefund(string Id, string? Status, decimal Amount, DateTimeOffset? CreatedAt);
public sealed record ProviderSavedMethod(
    string TokenId, string? CustomerId, string? Brand, string? LastDigits,
    string? Expiry, string? CardType, string? VerificationStatus);
public sealed record ProviderTransaction(
    string Id, string? ReferenceId, string? Status, decimal? Amount, decimal? Fee,
    string? Currency, DateTimeOffset? TransactionTime, string? InvoiceId, string? CustomId);

public sealed class PaymentProviderException : Exception
{
    public PaymentProviderException(string message, int statusCode = 502, Exception? inner = null)
        : base(message, inner) => StatusCode = statusCode;

    public int StatusCode { get; }
}
