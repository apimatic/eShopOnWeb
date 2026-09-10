using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// One-off card details supplied by a shopper. Held only long enough to pass to PayPal — the card number
/// and security code are never persisted by this application and never logged.
/// </summary>
public record CardDetails
{
    public required string Number { get; init; }
    /// <summary>Expiry in ISO-8601 <c>YYYY-MM</c> form.</summary>
    public required string Expiry { get; init; }
    public required string SecurityCode { get; init; }
    public string? CardholderName { get; init; }

    // Optional billing address (helps card processing / AVS).
    public string? BillingAddressLine1 { get; init; }
    public string? BillingAddressLine2 { get; init; }
    public string? BillingCity { get; init; }
    public string? BillingState { get; init; }
    public string? BillingPostalCode { get; init; }
    public string? BillingCountryCode { get; init; }
}

/// <summary>The result of authorizing (holding) an order's funds.</summary>
public record PaymentAuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public required string AuthorizationId { get; init; }
    public string? Status { get; init; }
    public string? ExpiresAt { get; init; }
    public decimal Amount { get; init; }
}

/// <summary>Current state of an authorization.</summary>
public record PaymentAuthorizationInfo
{
    public string? Status { get; init; }
    public string? ExpiresAt { get; init; }
}

/// <summary>The result of capturing an authorized payment, as PayPal reported it.</summary>
public record PaymentCaptureResult
{
    public required string CaptureId { get; init; }
    public string? Status { get; init; }
    public decimal GrossAmount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
}

/// <summary>The result of refunding a captured payment.</summary>
public record PaymentRefundResult
{
    public required string RefundId { get; init; }
    public string? Status { get; init; }
    public decimal Amount { get; init; }
}

/// <summary>Vault token id plus safe display details of a saved card.</summary>
public record SavedCardResult
{
    public required string VaultId { get; init; }
    public string? CardBrand { get; init; }
    public string? LastFourDigits { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
}

/// <summary>A single transaction from PayPal's transaction-search reporting.</summary>
public record PayPalTransaction
{
    public string? TransactionId { get; init; }
    public string? PayPalReferenceId { get; init; }
    public string? InvoiceId { get; init; }
    public decimal? Amount { get; init; }
    public string? CurrencyCode { get; init; }
    public decimal? FeeAmount { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
}
