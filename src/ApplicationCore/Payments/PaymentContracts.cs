using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public record CardPaymentDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string Name,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

public record AuthorizePaymentCommand(
    decimal Amount,
    string Currency,
    string InvoiceId,
    string CustomId,
    string IdempotencyKey,
    CardPaymentDetails? Card,
    string? VaultId);

public record AuthorizationResult(
    string CheckoutOrderId,
    string AuthorizationId,
    string AuthorizationStatus,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpirationTime,
    DateTimeOffset? CreateTime);

public record CaptureResult(
    string CaptureId,
    string CaptureStatus,
    decimal CapturedAmount,
    decimal? PaypalFee,
    decimal? NetProceeds,
    string Currency);

public record AuthorizationSnapshot(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpirationTime,
    DateTimeOffset? CreateTime);

public record RefundResult(
    string PayPalRefundId,
    string Status,
    decimal Amount,
    string Currency);

public record VaultedCardResult(
    string PaymentTokenId,
    string? PayPalCustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

public record PayPalReportedTransaction(
    string TransactionId,
    string? InvoiceId,
    string? CustomField,
    string? ReferenceId,
    string? EventCode,
    string? Status,
    string? Amount,
    string? FeeAmount,
    string? Currency,
    DateTimeOffset? InitiationDate);
