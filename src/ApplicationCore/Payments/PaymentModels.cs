using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public record CardPaymentInput(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

public record AuthorizePaymentRequest(
    int OrderId,
    decimal Amount,
    string Currency,
    string IdempotencyKey,
    CardPaymentInput? Card,
    string? VaultId);

public record AuthorizePaymentResult(
    string PaypalOrderId,
    string AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? ExpirationTime);

public record CapturePaymentResult(
    string CaptureId,
    string? Status,
    decimal? CapturedGross,
    decimal? PaypalFee,
    decimal? NetAmount);

public record VoidPaymentResult(string? AuthorizationStatus);

public record ReauthorizePaymentResult(
    string AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? ExpirationTime);

public record RefundPaymentRequest(
    string CaptureId,
    string Currency,
    decimal? Amount,
    string IdempotencyKey);

public record RefundPaymentResult(
    string RefundId,
    string? Status,
    decimal Amount);

public record AuthorizationDetails(
    string Id,
    string? Status,
    DateTimeOffset? ExpirationTime);

public record SaveCardRequest(
    string MerchantCustomerId,
    string? PaypalCustomerId,
    string IdempotencyKey,
    CardPaymentInput Card);

public record SavedCardResult(
    string PaymentTokenId,
    string? PaypalCustomerId,
    string? LastDigits,
    string? Brand,
    string? Expiry);

public record TransactionSearchItem(
    string? TransactionId,
    string? PaypalReferenceId,
    string? TransactionStatus,
    string? Amount,
    string? Fee,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    string? InitiationDate);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matches,
    IReadOnlyList<TransactionSearchItem> PaypalOnly,
    IReadOnlyList<EshopPaymentRecord> EshopOnly);

public record ReconciliationMatch(
    int OrderId,
    string? PaypalTransactionId,
    string MatchReason);

public record EshopPaymentRecord(
    int OrderId,
    string? PaypalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    string Status);
