using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// One-off card details supplied by a shopper for a single payment or to be vaulted. These values
/// are passed straight to PayPal and are never persisted or logged by this application.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,          // YYYY-MM
    string SecurityCode,
    string? CardholderName = null,
    string? BillingCountryCode = null,
    string? BillingAddressLine1 = null,
    string? BillingAddressLine2 = null,
    string? BillingAdminArea1 = null,
    string? BillingAdminArea2 = null,
    string? BillingPostalCode = null);

/// <summary>Instruction to authorize (hold) an order total at PayPal.</summary>
public record AuthorizeRequest
{
    public required int OrderId { get; init; }
    public required string CurrencyCode { get; init; }
    public required decimal Amount { get; init; }

    /// <summary>Raw card for a one-off payment. Mutually exclusive with <see cref="VaultTokenId"/>.</summary>
    public CardDetails? Card { get; init; }

    /// <summary>PayPal vault token id of a saved card to pay with instead of a raw card.</summary>
    public string? VaultTokenId { get; init; }

    /// <summary>Deterministic PayPal-Request-Id for idempotent create+authorize.</summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>Value written to purchase_unit.custom_id / invoice_id for reconciliation.</summary>
    public required string CustomId { get; init; }
    public required string InvoiceId { get; init; }
}

/// <summary>Result of an authorize (or reauthorize).</summary>
public record AuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public required string AuthorizationId { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>Result of a capture at fulfilment.</summary>
public record CaptureResult
{
    public required string CaptureId { get; init; }
    public string? Status { get; init; }
    public required decimal CapturedAmount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
}

/// <summary>Result of a refund.</summary>
public record RefundResult
{
    public required string RefundId { get; init; }
    public string? Status { get; init; }
    public required decimal Amount { get; init; }
}

/// <summary>Instruction to vault a card for future reuse.</summary>
public record VaultCardRequest
{
    public required CardDetails Card { get; init; }

    /// <summary>Existing PayPal vault customer id to group the token under, if the shopper already has one.</summary>
    public string? PayPalCustomerId { get; init; }

    /// <summary>Deterministic PayPal-Request-Id for idempotent vaulting.</summary>
    public string? IdempotencyKey { get; init; }
}

/// <summary>Result of vaulting a card — a safe descriptor plus the vault token id.</summary>
public record VaultCardResult
{
    public required string VaultTokenId { get; init; }
    public string? PayPalCustomerId { get; init; }
    public required string Brand { get; init; }
    public required string LastDigits { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
}

/// <summary>A transaction as PayPal's own reporting knows it (for reconciliation).</summary>
public record PayPalTransaction
{
    public string? TransactionId { get; init; }
    public string? Status { get; init; }
    public decimal? Amount { get; init; }
    public string? CurrencyCode { get; init; }
    public decimal? Fee { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomId { get; init; }
    public string? EventCode { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
}
