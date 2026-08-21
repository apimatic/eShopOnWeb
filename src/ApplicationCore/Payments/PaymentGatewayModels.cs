using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    BillingAddress? BillingAddress);

public record BillingAddress(
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode);

public record AuthorizationResult(
    string PayPalOrderId,
    string? PayPalOrderStatus,
    string AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? ExpirationTime,
    DateTimeOffset? CreateTime);

public record AuthorizationSnapshot(
    string AuthorizationId,
    string? Status,
    DateTimeOffset? ExpirationTime,
    DateTimeOffset? CreateTime);

public record CaptureResult(
    string CaptureId,
    string? CaptureStatus,
    decimal CapturedAmount,
    decimal? PaypalFee,
    decimal? NetAmount);

public record RefundResult(
    string RefundId,
    string? Status,
    decimal Amount);

public record VaultedCardResult(
    string PaymentTokenId,
    string LastDigits,
    string? Brand,
    string? Expiry,
    string? Name);

public record ReportedTransaction(
    string? TransactionId,
    string? InvoiceId,
    string? CustomField,
    string? Status,
    string? Amount,
    string? FeeAmount,
    string? Currency,
    string? InitiationDate,
    string? ReferenceId,
    string? ReferenceIdType);
