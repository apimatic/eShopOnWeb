using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class CardDetails
{
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public CardBillingAddress BillingAddress { get; set; } = new();
}

public sealed class CardBillingAddress
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
}

public sealed record PayPalOrderResult(string Id, string Status);

public sealed record PayPalAuthorizationResult(
    string OrderId,
    string OrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    decimal Amount,
    string Currency,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record PayPalAuthorizationDetails(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record PayPalCaptureResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    decimal? PayPalFee,
    decimal? NetAmount,
    DateTimeOffset? CreatedAt);

public sealed record PayPalRefundResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    decimal? RefundedPayPalFee,
    decimal? MerchantNetDebit,
    DateTimeOffset? UpdatedAt);

public sealed record PayPalPaymentTokenResult(
    string Id,
    string CustomerId,
    string Brand,
    string LastDigits,
    string Expiry);

public sealed record PayPalTransaction(
    string Id,
    string? ReferenceId,
    string? ReferenceIdType,
    string? InvoiceId,
    string? CustomId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    decimal? Fee,
    string? Currency,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record PayPalTransactionPage(
    IReadOnlyList<PayPalTransaction> Transactions,
    int Page,
    int TotalPages);
