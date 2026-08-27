using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Raw card details, used only in transit to PayPal. Never persisted, never logged.</summary>
public class CardDetails
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // YYYY-MM
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public CardBillingAddress? BillingAddress { get; set; }
}

public class CardBillingAddress
{
    public string CountryCode { get; set; } = string.Empty; // ISO-3166 alpha-2
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
}

public class AuthorizePaymentCommand
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string OrderReference { get; set; } = string.Empty;
    public string CreateOrderIdempotencyKey { get; set; } = string.Empty;
    public string AuthorizeIdempotencyKey { get; set; } = string.Empty;
    public CardDetails? Card { get; set; }
    public string? VaultTokenId { get; set; }
}

public class AuthorizationResult
{
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
}

public class AuthorizationState
{
    public string AuthorizationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public class CaptureResult
{
    public string CaptureId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
}

public class RefundResult
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class SaveCardCommand
{
    public string BuyerId { get; set; } = string.Empty;
    public string? PayPalCustomerId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public CardDetails Card { get; set; } = new();
}

public class SavedCardResult
{
    public string VaultTokenId { get; set; } = string.Empty;
    public string? PayPalCustomerId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class GatewayTransaction
{
    public string? TransactionId { get; set; }
    public string? ReferenceId { get; set; }
    public string? ReferenceIdType { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public string? InvoiceId { get; set; }
    public DateTimeOffset? InitiatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
