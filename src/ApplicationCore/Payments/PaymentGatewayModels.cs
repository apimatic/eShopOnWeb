using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Card details for a one-off PayPal payment. Never logged or persisted.
/// </summary>
public sealed class CardDetails
{
    public required string Number { get; init; }
    public required string Expiry { get; init; }
    public string? SecurityCode { get; init; }
    public string? Name { get; init; }
    public CardBillingAddress? BillingAddress { get; init; }

    public override string ToString() => "CardDetails(redacted)";
}

public sealed class CardBillingAddress
{
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? PostalCode { get; init; }
    public required string CountryCode { get; init; }
}

public sealed class AuthorizePaymentRequest
{
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string CustomId { get; init; }
    public required string InvoiceId { get; init; }
    public required string IdempotencyKey { get; init; }
    public CardDetails? Card { get; init; }
    public string? VaultId { get; init; }
}

public sealed class AuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public string? PayPalOrderStatus { get; init; }
    public required string AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public DateTimeOffset? Expiration { get; init; }
    public bool PayerActionRequired { get; init; }
}

public sealed class PaymentAuthorizationSnapshot
{
    public required string AuthorizationId { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? Expiration { get; init; }
}

public sealed class CaptureResult
{
    public required string CaptureId { get; init; }
    public string? Status { get; init; }
    public required decimal CapturedAmount { get; init; }
    public decimal? PaypalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public bool IsPending { get; init; }
}

public sealed class RefundResult
{
    public required string RefundId { get; init; }
    public string? Status { get; init; }
    public required decimal Amount { get; init; }
}

public sealed class VaultedCardResult
{
    public required string PaymentTokenId { get; init; }
    public string? PayPalCustomerId { get; init; }
    public string? MerchantCustomerId { get; init; }
    public string? LastDigits { get; init; }
    public string? Brand { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
}

public sealed class ProcessorTransaction
{
    public string? TransactionId { get; init; }
    public string? PaypalReferenceId { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public string? Status { get; init; }
    public string? AmountValue { get; init; }
    public string? AmountCurrency { get; init; }
    public string? FeeValue { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
}
