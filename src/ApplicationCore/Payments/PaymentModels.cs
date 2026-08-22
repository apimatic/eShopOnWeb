using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record CardBillingAddress(
    string CountryCode,
    string? AddressLine1,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode);

public sealed record CardPaymentDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

public sealed record AuthorizationHold(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    string? ExpirationTime,
    string? CreateTime,
    string Currency,
    decimal Amount);

public sealed record CaptureDetails(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PaypalFee,
    decimal? NetAmount,
    string Currency);

public sealed record RefundDetails(
    string PayPalRefundId,
    string Status,
    decimal Amount,
    string Currency,
    decimal? TotalRefundedAmount);

public sealed record VaultedCardDetails(
    string VaultId,
    string? PayPalCustomerId,
    string? LastDigits,
    string? Brand,
    string? Expiry,
    string? Name);

public sealed record PayPalReportedTransaction(
    string TransactionId,
    string? InvoiceId,
    string? CustomField,
    string? Status,
    decimal? Amount,
    decimal? FeeAmount,
    string? InitiationDate);

public sealed record CatalogOrderLine(int CatalogItemId, int Quantity);
