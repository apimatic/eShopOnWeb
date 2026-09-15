using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>Raw card details, used only in-flight to talk to PayPal — never persisted or logged.</summary>
public record PayPalCard(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    PayPalBillingAddress? BillingAddress);

public record PayPalBillingAddress(
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? PostalCode,
    string? CountryCode);

/// <summary>
/// A request to place a hold (authorization) for an order total. Exactly one instrument is supplied:
/// either a one-off <see cref="Card"/> or the <see cref="VaultId"/> of a previously saved card.
/// </summary>
public record PayPalAuthorizeRequest
{
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string ReconciliationId { get; init; }
    public required string RequestId { get; init; }

    public PayPalCard? Card { get; init; }
    public string? VaultId { get; init; }

    /// <summary>Vault the card as part of this authorization (one-off card only).</summary>
    public bool StoreInVault { get; init; }
}

/// <summary>Result of placing a hold: the PayPal order + authorization state, and any vaulted-card detail.</summary>
public record PayPalAuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public required string OrderStatus { get; init; }
    public string? AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? CardBrand { get; init; }
    public string? CardLast4 { get; init; }

    // Populated only when the card was vaulted as part of this authorization.
    public string? VaultId { get; init; }
    public string? VaultCustomerId { get; init; }

    /// <summary>True when PayPal returned a buyer-approval / 3DS challenge — the flow must STOP.</summary>
    public bool RequiresApproval { get; init; }
    public string? ApprovalUrl { get; init; }
}

public record PayPalAuthorizationInfo(string Status, DateTimeOffset? ExpiresAt);

public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal Amount,
    decimal? Fee,
    decimal? Net,
    string Currency);

public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>Safe representation of a card saved into PayPal's vault.</summary>
public record PayPalVaultedCard(
    string VaultId,
    string? CustomerId,
    string Brand,
    string Last4,
    string Expiry,
    string? Name);

/// <summary>One transaction as PayPal's reporting API knows it, for reconciliation.</summary>
public record PayPalTransaction(
    string TransactionId,
    string Status,
    decimal? Amount,
    string? Currency,
    string? InvoiceId,
    string? CustomField,
    DateTimeOffset? InitiationDate,
    string? EventCode);
