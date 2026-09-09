using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Plain, SDK-agnostic payment models exchanged between the API layer and the payment gateway
/// abstraction (<see cref="Interfaces.IPaymentGateway"/>). Keeping these free of any PayPal SDK type
/// keeps the SDK dependency inside Infrastructure. Card secrets live only in <see cref="CardDetails"/>
/// while a request is in flight; they are never persisted.
/// </summary>
public sealed record CardDetails
{
    /// <summary>Primary account number (PAN). Never stored, never logged.</summary>
    public required string Number { get; init; }

    /// <summary>Expiry in ISO-8601 <c>YYYY-MM</c> form.</summary>
    public required string Expiry { get; init; }

    /// <summary>Card security code (CVV/CVC). Never stored, never logged.</summary>
    public required string SecurityCode { get; init; }

    public string? CardholderName { get; init; }

    // Optional billing address.
    public string? BillingLine1 { get; init; }
    public string? BillingCity { get; init; }
    public string? BillingState { get; init; }
    public string? BillingCountryCode { get; init; }
    public string? BillingPostalCode { get; init; }
}

/// <summary>Request to authorize (hold) an order total by card or by a saved (vaulted) card.</summary>
public sealed record AuthorizeCardRequest
{
    /// <summary>A stable reference (the eShop order id) stamped onto the PayPal transaction for reconciliation.</summary>
    public required string OrderReference { get; init; }
    public required string CurrencyCode { get; init; }
    public required decimal Amount { get; init; }
    public string? Description { get; init; }

    /// <summary>Raw card for a one-off payment. Mutually exclusive with <see cref="VaultId"/>.</summary>
    public CardDetails? Card { get; init; }

    /// <summary>PayPal vault id of a saved card. Mutually exclusive with <see cref="Card"/>.</summary>
    public string? VaultId { get; init; }

    /// <summary>Idempotency key so a double-click never authorizes twice.</summary>
    public required string IdempotencyKey { get; init; }
}

public sealed record AuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public required string AuthorizationId { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed record CaptureResult
{
    public required string CaptureId { get; init; }
    public string? Status { get; init; }
    public required decimal CapturedAmount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
}

public sealed record ReauthorizationResult
{
    public required string AuthorizationId { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed record AuthorizationView
{
    public string? Status { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed record RefundResult
{
    public required string RefundId { get; init; }
    public string? Status { get; init; }
    public required decimal Amount { get; init; }
}

/// <summary>Request to vault (save) a card for later reuse.</summary>
public sealed record VaultCardRequest
{
    /// <summary>A stable reference to the shopper (used as PayPal merchant_customer_id).</summary>
    public required string BuyerReference { get; init; }
    public required CardDetails Card { get; init; }
}

public sealed record SavedCardResult
{
    public required string VaultId { get; init; }
    public string? CustomerId { get; init; }
    public string? Brand { get; init; }
    public string? LastDigits { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
}

/// <summary>A single PayPal transaction as reported by the reconciliation report.</summary>
public sealed record ReconciliationTransaction
{
    public string? TransactionId { get; init; }
    public string? Status { get; init; }
    public decimal? Amount { get; init; }
    public string? CurrencyCode { get; init; }
    public decimal? FeeAmount { get; init; }
    public DateTimeOffset? Date { get; init; }

    /// <summary>The eShop order reference PayPal carries (custom_field, falling back to invoice_id), if any.</summary>
    public string? OrderReference { get; init; }
}
