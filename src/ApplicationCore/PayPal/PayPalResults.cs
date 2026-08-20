using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

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
    decimal Amount,
    string Currency);

public sealed record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PaypalFee,
    decimal? NetAmount,
    string Currency);

public sealed record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public sealed record PayPalVaultedCardResult(
    string PaymentTokenId,
    string? CustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? Name);

public sealed record PayPalReportedTransaction(
    string TransactionId,
    string? PaypalReferenceId,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? InitiationDate,
    decimal? FeeAmount);

public sealed record PayPalTransactionPage(
    IReadOnlyList<PayPalReportedTransaction> Transactions,
    int Page,
    int? TotalPages);
