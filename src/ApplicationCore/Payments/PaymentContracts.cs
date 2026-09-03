using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record CatalogItemQuantity(int CatalogItemId, int Quantity);

public sealed record ShippingAddressInput(
    string Street,
    string City,
    string State,
    string Country,
    string PostalCode);

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

public sealed record PayOrderInput(CardInput? Card, int? PaymentMethodId);

public sealed record RefundInput(decimal? Amount, string IdempotencyKey);

public sealed record OrderItemView(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);

public sealed record RefundView(string RefundId, decimal Amount, string Status, DateTimeOffset CreatedAt);

public sealed record OrderView(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string? Currency,
    PaymentStatus PaymentStatus,
    FulfillmentStatus FulfillmentStatus,
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
    decimal RefundableAmount,
    IReadOnlyList<OrderItemView> Items,
    IReadOnlyList<RefundView> Refunds);

public sealed record PaymentMethodView(
    int PaymentMethodId,
    string Brand,
    string Last4,
    string Expiry,
    DateTimeOffset CreatedAt);

public sealed record RefundCreated(string RefundId, OrderView Order);

public sealed record ProviderTransaction(
    string TransactionId,
    string? ReferenceId,
    string? InvoiceId,
    string? EventCode,
    string? Status,
    DateTimeOffset? InitiatedAt,
    decimal? Amount,
    decimal? Fee,
    string? Currency);

public sealed record ReconciliationLine(
    int? OrderId,
    string? PayPalTransactionId,
    string? PayPalReferenceId,
    string? EventCode,
    string? PayPalStatus,
    DateTimeOffset? PayPalInitiatedAt,
    decimal? PayPalAmount,
    decimal? PayPalFee,
    decimal? OrderAmount,
    string? Currency,
    string MatchStatus);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationLine> Lines);

public enum PaymentFailureKind
{
    Validation,
    NotFound,
    Conflict,
    PayerActionRequired,
    ProviderRejected,
    ProviderUnavailable,
    UnknownOutcome
}

public sealed class PaymentException : Exception
{
    public PaymentException(PaymentFailureKind kind, string message, string? providerDebugId = null,
        Exception? innerException = null) : base(message, innerException)
    {
        Kind = kind;
        ProviderDebugId = providerDebugId;
    }

    public PaymentFailureKind Kind { get; }
    public string? ProviderDebugId { get; }
}
