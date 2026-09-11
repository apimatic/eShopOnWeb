using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>Raw card details for a one-off payment or to vault. Never persisted by this app.</summary>
public record PayPalCard(
    string Number,
    string Expiry,          // YYYY-MM
    string SecurityCode,
    string? Name,
    PayPalBillingAddress? BillingAddress);

public record PayPalBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,     // city
    string? AdminArea1,     // state
    string? PostalCode,
    string CountryCode);

/// <summary>
/// Instruction to create-and-authorize a PayPal order for a card payment. Exactly one of
/// <see cref="Card"/> (a one-off card) or <see cref="VaultId"/> (a saved card) is set.
/// </summary>
public record AuthorizeOrderRequest(
    decimal Amount,
    string InvoiceId,
    string CustomId,
    PayPalCard? Card,
    string? VaultId);

public record AuthorizeOrderResult(
    string PayPalOrderId,
    string OrderStatus,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    decimal AuthorizedAmount,
    string Currency,
    string? CardBrand,
    string? CardLast4,
    bool RequiresApproval,
    string? ApprovalUrl);

public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string Currency);

public record AuthorizationResult(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public record VaultCardResult(
    string VaultTokenId,
    string CustomerId,
    string? Brand,
    string? Last4,
    string? Expiry);

public record PayPalTransaction(
    string TransactionId,
    string? InvoiceId,
    string? CustomField,
    decimal Amount,
    string Currency,
    string Status,
    DateTimeOffset? InitiationDate);
