using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public record CardBillingAddress(
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,
    string? AdminArea2,
    string? PostalCode);

public record CardPaymentSource(
    string Number,
    string Expiry,
    string SecurityCode,
    string Name,
    CardBillingAddress? BillingAddress);

public record VaultedCardPaymentSource(string VaultId);

public record AuthorizedPaymentResult(
    string PayPalOrderId,
    string PayPalOrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? ExpirationTime,
    decimal Amount,
    string Currency);

public record AuthorizationDetails(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpirationTime,
    DateTimeOffset? CreateTime,
    decimal Amount,
    string Currency,
    string? CaptureId);

public record CapturedPaymentResult(
    string CaptureId,
    string CaptureStatus,
    decimal CapturedAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string Currency);

public record RefundPaymentResult(
    string PayPalRefundId,
    string Status,
    decimal Amount,
    string Currency);

public record VaultedCardResult(
    string PaymentTokenId,
    string? CustomerId,
    string LastDigits,
    string Brand,
    string Expiry,
    string? Name);

public record PayPalReportedTransaction(
    string TransactionId,
    string? ReferenceId,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    string? Status,
    DateTimeOffset? InitiationDate,
    decimal? Amount,
    string? Currency,
    decimal? FeeAmount);
