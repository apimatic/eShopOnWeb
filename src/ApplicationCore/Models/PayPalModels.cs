using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// Full card details used only in transit to PayPal. Never persisted, never logged.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    BillingAddress? Address);

public record BillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

public record PayPalAuthorizationInfo(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpirationTime);

public record PayPalCaptureInfo(
    string Id,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

public record PayPalRefundInfo(
    string Id,
    string Status,
    decimal Amount,
    string Currency);

public record PayPalCardTokenInfo(
    string TokenId,
    string? Brand,
    string? Last4,
    string? Expiry,
    string? CardholderName);

public record PayPalTransactionInfo(
    string TransactionId,
    string? PayPalReferenceId,
    string? PayPalReferenceIdType,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? InvoiceId,
    string? CustomField,
    DateTimeOffset? InitiationDate,
    DateTimeOffset? UpdatedDate);

public record ReconciliationEntry(
    PayPalTransactionInfo Transaction,
    int? MatchedOrderId,
    string? MatchedEntity);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Transactions,
    IReadOnlyList<UnmatchedPaymentInfo> PaymentsMissingFromPayPal,
    IReadOnlyList<PayPalTransactionInfo> TransactionsMissingFromEShop);

public record UnmatchedPaymentInfo(
    int OrderId,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    IReadOnlyList<string> RefundIds);
