using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>
/// Raw card details, used only in transit between the API boundary and the
/// payment gateway. Never persisted and never logged.
/// </summary>
public sealed class CardDetails
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // YYYY-MM
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public sealed record PaymentAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public sealed record AuthorizationState(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public sealed record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? Fee,
    decimal? NetAmount,
    string Currency);

public sealed record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public sealed record VaultedCardResult(
    string VaultTokenId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

public sealed record GatewayTransaction(
    string TransactionId,
    string? ReferenceId,
    string? ReferenceIdType,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    DateTimeOffset? Time);
