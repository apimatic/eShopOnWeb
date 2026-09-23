using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Raw one-off card details supplied by the shopper. Never persisted or logged by this app.</summary>
public record CardDetails(
    string Number,
    string Expiry,               // "YYYY-MM"
    string SecurityCode,
    string? CardholderName,
    string? BillingLine1 = null,
    string? BillingCity = null,
    string? BillingState = null,
    string? BillingCountryCode = null,
    string? BillingPostalCode = null);

public enum AuthorizationOutcome { Authorized, ChallengeRequired, Pending, Failed }
public enum CaptureOutcome { Completed, Pending, Failed }
public enum RefundOutcome { Completed, Pending, Failed }

/// <summary>What the gateway needs to place a hold: amount, references, idempotency key, and a funding source.</summary>
public record AuthorizeGatewayRequest(
    decimal Amount,
    string Currency,
    string OrderReference,       // eShop order id → PayPal custom_id
    string InvoiceId,            // PayPal invoice_id (reconciliation key)
    string RequestId,            // PayPal-Request-Id (idempotency)
    CardDetails? Card,
    string? VaultId);            // saved-card vault token, mutually exclusive with Card

public record AuthorizationResult(
    string PayPalOrderId,
    string? AuthorizationId,
    AuthorizationOutcome Outcome,
    string? AuthorizationStatus,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? CreatedAt,
    string? FailureReason);

public record AuthorizationInfo(
    string Id,
    string? Status,
    DateTimeOffset? ExpiresAt,
    bool IsCaptured,
    bool IsUsable);

public record CaptureResult(
    string CaptureId,
    CaptureOutcome Outcome,
    string? RawStatus,
    decimal? Gross,
    decimal? Fee,
    decimal? Net,
    string Currency,
    DateTimeOffset? CreatedAt);

public record RefundResult(
    string RefundId,
    RefundOutcome Outcome,
    string? RawStatus,
    decimal? Amount,
    string Currency);

public record VaultCardResult(
    string TokenId,
    string? CustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

public record PayPalTransaction(
    string? TransactionId,
    string? InvoiceId,
    decimal? Amount,
    string? Currency,
    string? Status,
    DateTimeOffset? InitiationDate);

/// <summary>A reconciliation page-walk result. <see cref="Truncated"/> tells the caller if a page cap cut it short.</summary>
public record ReconciliationFetch(
    IReadOnlyList<PayPalTransaction> Transactions,
    int PagesFetched,
    int TotalPages,
    bool Truncated);
