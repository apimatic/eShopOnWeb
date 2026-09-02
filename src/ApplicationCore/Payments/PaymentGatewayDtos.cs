using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details, held only for the duration of a single provider call.
/// Never persisted and never logged.
/// </summary>
public sealed record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string HolderName,
    BillingAddress? BillingAddress);

public sealed record BillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);

public sealed record GatewayOrder(
    string PayPalOrderId,
    string Status,
    bool PayerActionRequired,
    GatewayAuthorization? Authorization);

public sealed record GatewayAuthorization(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? Amount,
    string? Currency,
    bool PayerActionRequired);

public sealed record GatewayAuthorizationState(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public sealed record GatewayCapture(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string? Currency);

public sealed record GatewayRefund(
    string RefundId,
    string Status,
    decimal? Amount,
    string? Currency);

public sealed record GatewayVaultedCard(
    string PaymentTokenId,
    string? PayPalCustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

public sealed record GatewayTransaction(
    string? TransactionId,
    string? ReferenceId,
    string? ReferenceIdType,
    string? EventCode,
    string? Status,
    decimal? Amount,
    decimal? Fee,
    string? Currency,
    string? InvoiceId,
    string? CustomField,
    DateTimeOffset? UpdatedAt);

public sealed record ReconciliationEntry(
    GatewayTransaction Transaction,
    int? MatchedOrderId);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Transactions,
    IReadOnlyList<int> OrdersMissingFromPayPal,
    int PayPalTransactionCount);
