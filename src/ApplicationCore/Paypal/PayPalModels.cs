using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Paypal;

/// <summary>Raw card details passed through to PayPal. Never persisted or logged by this app.</summary>
public record PayPalCardDetails(
    string Number,
    string Expiry,          // "YYYY-MM"
    string SecurityCode,
    string? Name = null,
    PayPalBillingAddress? BillingAddress = null);

/// <summary>Billing address in PayPal's address shape.</summary>
public record PayPalBillingAddress(
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? AdminArea1 = null,   // state / province
    string? AdminArea2 = null,   // city
    string? PostalCode = null,
    string? CountryCode = null);

/// <summary>
/// A request to authorize an order total. Exactly one of <see cref="Card"/> or <see cref="VaultId"/>
/// is supplied — a one-off card, or a saved (vaulted) card.
/// </summary>
public record PayPalAuthorizationRequest
{
    public required string ReferenceId { get; init; }
    public required decimal Amount { get; init; }
    public required string CurrencyCode { get; init; }
    /// <summary>Stable idempotency key so a double-click never authorizes twice.</summary>
    public required string IdempotencyKey { get; init; }
    /// <summary>Echoed onto the PayPal transaction so reconciliation can line it up with an eShop order.</summary>
    public string? CustomId { get; init; }
    public PayPalCardDetails? Card { get; init; }
    public string? VaultId { get; init; }
}

public record PayPalAuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public required string AuthorizationId { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? InstrumentSummary { get; init; }
    public string? VaultId { get; init; }
}

public record PayPalCaptureResult
{
    public required string CaptureId { get; init; }
    public string? Status { get; init; }
    public required decimal Amount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public required string CurrencyCode { get; init; }
}

public record PayPalRefundResult
{
    public required string RefundId { get; init; }
    public string? Status { get; init; }
    public required decimal Amount { get; init; }
    public required string CurrencyCode { get; init; }
}

public record PayPalVaultResult
{
    public required string VaultId { get; init; }
    public string? Brand { get; init; }
    public string? Last4 { get; init; }
    public int? ExpiryMonth { get; init; }
    public int? ExpiryYear { get; init; }
}

/// <summary>A single PayPal-side transaction returned by the reporting API, for reconciliation.</summary>
public record PayPalTransaction
{
    public required string TransactionId { get; init; }
    public string? Status { get; init; }
    public string? EventCode { get; init; }
    public decimal? Amount { get; init; }
    public string? CurrencyCode { get; init; }
    public decimal? Fee { get; init; }
    public DateTimeOffset? Date { get; init; }
    /// <summary>The value we stamped as custom_id on the order (our eShop order id), when present.</summary>
    public string? CustomId { get; init; }
    public string? InvoiceId { get; init; }
}
