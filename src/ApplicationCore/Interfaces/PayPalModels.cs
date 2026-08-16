using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Raw card details for a one-off payment or for vaulting. Never persisted or logged.</summary>
public record PayPalCardDetails(
    string Number,
    string Expiry, // "YYYY-MM"
    string? SecurityCode,
    string? Name,
    PayPalBillingAddress? BillingAddress);

public record PayPalBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2, // city
    string? AdminArea1, // state/province
    string? PostalCode,
    string? CountryCode);

/// <summary>Request to authorize (hold) an order total with a card or a vaulted card.</summary>
public record PayPalAuthorizeRequest(
    decimal Amount,
    string Currency,
    string InvoiceId,
    string CustomId,
    PayPalCardDetails? Card,
    string? VaultId);

public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    bool RequiresBuyerAction,
    string? CardBrand,
    string? CardLastFour);

public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal PayPalFee,
    decimal NetAmount,
    DateTimeOffset CapturedAt);

public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount);

public record PayPalVaultResult(
    string VaultId,
    string? CustomerId,
    string CardBrand,
    string CardLastFour,
    string Expiry,
    string? CardholderName);

/// <summary>A single PayPal transaction from the Transaction Search (reporting) API.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? InvoiceId,
    string? CustomId,
    decimal Amount,
    string Currency,
    string Status,
    string? EventCode,
    DateTimeOffset Date);

/// <summary>Status of an existing PayPal authorization.</summary>
public record PayPalAuthorizationState(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);
