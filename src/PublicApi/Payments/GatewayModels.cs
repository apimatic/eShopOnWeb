using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Card details passed through to PayPal. Never persisted, never logged.</summary>
public class GatewayCard
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // YYYY-MM
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}

public record GatewayAuthorizationResult(string PayPalOrderId, string AuthorizationId, string Status, DateTimeOffset? ExpiresAt);

public record GatewayAuthorizationStatus(string AuthorizationId, string Status, DateTimeOffset? ExpiresAt);

public record GatewayCaptureResult(string CaptureId, string Status, decimal GrossAmount, decimal? PayPalFee, decimal? NetAmount);

public record GatewayRefundResult(string RefundId, string Status, decimal Amount);

public record GatewaySavedCardResult(string VaultTokenId, string Brand, string LastDigits, string Expiry);

public record GatewayTransaction(
    string TransactionId,
    string? InitiatedAt,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? Status,
    string? InvoiceId,
    string? CustomField);
