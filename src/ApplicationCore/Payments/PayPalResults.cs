using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpirationTime,
    decimal Amount,
    string Currency);

public sealed record PayPalAuthorizationDetails(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpirationTime,
    DateTimeOffset? CreateTime,
    decimal Amount,
    string Currency);

public sealed record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal PayPalFee,
    decimal NetProceeds,
    string Currency);

public sealed record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public sealed record PayPalVaultedCard(
    string PaymentTokenId,
    string? PayPalCustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

public sealed record PayPalReportedTransaction(
    string TransactionId,
    string? PayPalReferenceId,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? InitiationTime,
    IReadOnlyDictionary<string, string?> Extra);
