using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public record CardPaymentDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,
    string? AdminArea2,
    string? PostalCode,
    string? CountryCode);

public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string AuthorizationStatus,
    decimal AuthorizedAmount,
    string Currency,
    DateTimeOffset? CreateTime,
    DateTimeOffset? ExpirationTime);

public record CaptureResult(
    string CaptureId,
    string CaptureStatus,
    decimal CapturedAmount,
    decimal PaypalFee,
    decimal NetAmount,
    string Currency);

public record RefundGatewayResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public record VaultedCardResult(
    string VaultId,
    string? CustomerId,
    string LastDigits,
    string Brand,
    string Expiry,
    string? CardholderName);

public record ReportedTransaction(
    string TransactionId,
    string? ReferenceId,
    string? ReferenceIdType,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    string? Status,
    DateTimeOffset? InitiationDate,
    decimal? Amount,
    string? Currency,
    decimal? FeeAmount);
