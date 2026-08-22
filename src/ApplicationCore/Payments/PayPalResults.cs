using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record PayPalMoney(string CurrencyCode, decimal Value);

public sealed record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpirationTime,
    DateTimeOffset? CreateTime,
    PayPalMoney Amount);

public sealed record PayPalAuthorizationDetails(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpirationTime,
    DateTimeOffset? CreateTime,
    PayPalMoney? Amount);

public sealed record PayPalCaptureResult(
    string CaptureId,
    string Status,
    PayPalMoney Amount,
    PayPalMoney? PayPalFee,
    PayPalMoney? NetAmount);

public sealed record PayPalRefundResult(
    string RefundId,
    string Status,
    PayPalMoney Amount);

public sealed record PayPalVaultedCard(
    string PaymentTokenId,
    string? CustomerId,
    string Brand,
    string LastDigits,
    string Expiry,
    string? CardholderName);

public sealed record PayPalReportedTransaction(
    string TransactionId,
    string? PaypalReferenceId,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    string? Status,
    string? CurrencyCode,
    decimal? Amount,
    DateTimeOffset? InitiationDate,
    DateTimeOffset? UpdatedDate);

public sealed record ReconciliationRow(
    string Match,
    int? OrderId,
    string? PayPalTransactionId,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    string? Note);
