using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Raw card details for a one-off payment or for vaulting. Transient only:
/// never persisted, never logged.
/// </summary>
public record GatewayCardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string Name,
    GatewayBillingAddress? BillingAddress);

public record GatewayBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);

/// <summary>
/// One authorization attempt: exactly one of <see cref="Card"/> / <see cref="PaymentTokenId"/> is set.
/// </summary>
public record GatewayAuthorizeRequest(
    decimal Amount,
    string Currency,
    string InvoiceId,
    string IdempotencyKey,
    GatewayCardDetails? Card,
    string? PaymentTokenId);

public record GatewayAuthorization(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpiresAt);

public record GatewayAuthorizationState(
    string AuthorizationId,
    string Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? ExpiresAt);

public record GatewayCapture(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PaypalFee,
    decimal? NetAmount,
    string Currency);

public record GatewayRefund(
    string RefundId,
    string Status,
    decimal? Amount,
    string? Currency);

public record GatewayVaultedCard(
    string PaymentTokenId,
    string PayPalCustomerId,
    string? LastDigits,
    string? Brand,
    string? Expiry);

public record GatewayTransaction(
    string? TransactionId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? InvoiceId,
    string? CustomField,
    DateTimeOffset? InitiationDate,
    DateTimeOffset? UpdatedDate);
