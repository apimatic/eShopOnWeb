using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>
/// Caller-supplied card details. These flow through to the payment provider and are never
/// persisted or logged by this application.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    BillingAddress? Address);

public record BillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);

public record AuthorizationRequest(
    int LocalOrderId,
    decimal Amount,
    string Currency,
    string IdempotencyKey,
    CardDetails? Card,
    string? VaultedCardTokenId);

public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record AuthorizationInfo(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt)
{
    /// <summary>True while PayPal still considers the hold open (CREATED).</summary>
    public bool IsOpen => Status == "CREATED";
}

public record CaptureResult(
    string CaptureId,
    string Status,
    decimal? Gross,
    decimal? Fee,
    decimal? Net,
    string Currency);

public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency,
    decimal? TotalRefunded);

public record VaultedCardResult(
    string PaymentTokenId,
    string PayPalCustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry);

/// <summary>One line of the provider's transaction report.</summary>
public record GatewayTransaction(
    string? TransactionId,
    string? ReferenceId,
    string? ReferenceIdType,
    string? InvoiceId,
    string? CustomId,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? Status,
    string? EventCode,
    DateTimeOffset? InitiatedAt);
