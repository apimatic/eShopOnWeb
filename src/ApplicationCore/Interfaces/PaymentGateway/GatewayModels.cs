using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;

/// <summary>Raw card details passed to the gateway for a one-off payment or to vault. Never stored.</summary>
public record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name = null,
    GatewayBillingAddress? BillingAddress = null);

public record GatewayBillingAddress(
    string? AddressLine1,
    string? AdminArea1,
    string? AdminArea2,
    string? PostalCode,
    string? CountryCode);

/// <summary>
/// Instruction to create a PayPal order (intent=AUTHORIZE) and place the hold, paying either with
/// raw card details or a previously vaulted card. Exactly one of <see cref="Card"/> /
/// <see cref="VaultId"/> is set.
/// </summary>
public record CreateAuthorizationRequest(
    decimal Amount,
    string CurrencyCode,
    string OrderReference,
    string InvoiceId,
    string CreateRequestId,
    string AuthorizeRequestId,
    CardDetails? Card,
    string? VaultId);

/// <summary>State of a PayPal authorization (the hold).</summary>
public record GatewayAuthorization(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    bool RequiresBuyerAction);

/// <summary>State of a PayPal capture (money taken), with the merchant breakdown.</summary>
public record GatewayCapture(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string CurrencyCode);

/// <summary>State of a PayPal refund.</summary>
public record GatewayRefund(
    string RefundId,
    string Status,
    decimal Amount,
    decimal? TotalRefunded);

/// <summary>A safely describable vaulted card — no PAN.</summary>
public record GatewayVaultedCard(
    string VaultId,
    string Brand,
    string LastFourDigits,
    string Expiry);

/// <summary>One row of PayPal's own transaction record, for reconciliation.</summary>
public record GatewayTransaction(
    string? TransactionId,
    string? InvoiceId,
    string? CustomField,
    decimal? Amount,
    decimal? FeeAmount,
    string? CurrencyCode,
    string? Status,
    string? InitiationDate,
    string? UpdatedDate);
