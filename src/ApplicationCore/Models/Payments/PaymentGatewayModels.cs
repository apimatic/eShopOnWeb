using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>Full card details, used only in transit to the payment provider. Never persisted, never logged.</summary>
public sealed record CardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? CardholderName,
    BillingAddress? Address);

public sealed record BillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

/// <summary>Result of placing a hold (authorization) on the order total.</summary>
public sealed record AuthorizationResult(
    string ProviderOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpirationTime);

/// <summary>Current state of an authorization as reported by the provider.</summary>
public sealed record AuthorizationState(
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpirationTime);

/// <summary>Result of capturing an authorization, including the provider's fee breakdown.</summary>
public sealed record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? ProviderFee,
    decimal? NetAmount,
    string Currency);

public sealed record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>A vaulted card; only safe display data is returned.</summary>
public sealed record VaultedCardResult(
    string VaultTokenId,
    string? LastDigits,
    string? Brand,
    string? Expiry);

/// <summary>A single transaction from the provider's own transaction report.</summary>
public sealed record GatewayTransaction(
    string TransactionId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? InitiationDate,
    string? ReferenceId,
    string? ReferenceIdType,
    string? InvoiceId,
    string? CustomField);
