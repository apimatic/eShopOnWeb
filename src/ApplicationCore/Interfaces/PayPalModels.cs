using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed class CardPaymentSource
{
    public required string Name { get; init; }
    public required string Number { get; init; }
    public required string Expiry { get; init; }
    public required string SecurityCode { get; init; }
    public required CardBillingAddress BillingAddress { get; init; }

    public override string ToString() => "[redacted card]";
}

public sealed class CardBillingAddress
{
    public required string CountryCode { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? PostalCode { get; init; }
}

public sealed record PayPalMoney(string CurrencyCode, decimal Value);

public sealed record AuthorizedPaymentResult(
    string PayPalOrderId,
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? ExpirationTime,
    decimal Amount,
    string Currency);

public sealed record CapturedPaymentResult(
    string CaptureId,
    string CaptureStatus,
    decimal CapturedAmount,
    decimal? PaypalFee,
    decimal? NetAmount,
    string Currency);

public sealed record RefundedPaymentResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public sealed record VaultedCardResult(
    string VaultId,
    string? CustomerId,
    string? LastDigits,
    string? Brand,
    string? Expiry,
    string? Name);

public sealed record PayPalCheckoutItem(
    string Name,
    string Quantity,
    decimal UnitAmount,
    string? Sku);

public sealed record PayPalReportedTransaction(
    string TransactionId,
    string? ReferenceId,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? InitiationDate,
    decimal? Fee);

public sealed record AuthorizationDetails(
    string Id,
    string Status,
    DateTimeOffset? ExpirationTime,
    decimal? Amount,
    string? Currency);
