using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Card details for a one-off payment or for vaulting. Never persisted by this app.</summary>
public record GatewayCardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    GatewayBillingAddress? BillingAddress);

public record GatewayBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

/// <summary>Safe, processor-returned description of a vaulted card.</summary>
public record GatewayCardDescription(
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

/// <summary>
/// Request to hold funds: create a processor order with intent to authorize, using either raw card
/// details or a vaulted card, and authorize it. Optionally vault the card on success.
/// </summary>
public record GatewayAuthorizeRequest
{
    public required decimal Amount { get; init; }
    public required string CurrencyCode { get; init; }
    /// <summary>Globally-unique invoice reference (PayPal enforces uniqueness per merchant).</summary>
    public required string InvoiceId { get; init; }
    /// <summary>eShop order id carried through as custom_id so transactions reconcile back to it.</summary>
    public required string CustomId { get; init; }
    /// <summary>Idempotency key sent to the processor.</summary>
    public required string RequestId { get; init; }

    /// <summary>Raw card for a one-off payment (mutually exclusive with <see cref="VaultId"/>).</summary>
    public GatewayCardDetails? Card { get; init; }

    /// <summary>Vault token id of a saved card to pay with.</summary>
    public string? VaultId { get; init; }

    /// <summary>When true, vault the card used for this order (only valid with <see cref="Card"/>).</summary>
    public bool SaveCard { get; init; }

    /// <summary>PayPal customer id to attach the vaulted card to (required when <see cref="SaveCard"/>).</summary>
    public string? CustomerId { get; init; }
}

public record GatewayAuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public required string AuthorizationId { get; init; }
    public required string AuthorizationStatus { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public required decimal Amount { get; init; }
    public required string CurrencyCode { get; init; }

    // Populated when a card was vaulted as part of this order.
    public string? VaultedCardId { get; init; }
    public GatewayCardDescription? VaultedCard { get; init; }
}

public record GatewayCaptureResult
{
    public required string CaptureId { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public required string CurrencyCode { get; init; }
}

public record GatewayRefundResult
{
    public required string RefundId { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
    public required string CurrencyCode { get; init; }
}

public record GatewayVaultCardResult
{
    public required string TokenId { get; init; }
    public string? Brand { get; init; }
    public string? LastDigits { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
    public required string CustomerId { get; init; }
}

/// <summary>A single transaction as PayPal's transaction-search report records it.</summary>
public record GatewayTransaction
{
    public string? TransactionId { get; init; }
    public string? Status { get; init; }
    public string? EventCode { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
    public decimal? Amount { get; init; }
    public string? CurrencyCode { get; init; }
    public decimal? FeeAmount { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public string? ReferenceId { get; init; }
}
