using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>Result of creating + authorizing a PayPal order for a direct card payment (one call,
/// since direct card entry processes inline with no buyer redirect).</summary>
public class PayPalAuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public required string OrderStatus { get; init; }
    public bool RequiresPayerAction { get; init; }
    public string? AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
}

public class PayPalReauthorizationResult
{
    public required string AuthorizationId { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
}

public class PayPalCaptureResult
{
    public required string CaptureId { get; init; }
    public required string Status { get; init; }
    public decimal GrossAmount { get; init; }
    public decimal FeeAmount { get; init; }
    public decimal NetAmount { get; init; }
    public required string CurrencyCode { get; init; }
    public DateTimeOffset CaptureTime { get; init; }
}

public class PayPalRefundResult
{
    public required string RefundId { get; init; }
    public required string Status { get; init; }
    public decimal Amount { get; init; }
    public required string CurrencyCode { get; init; }
    public DateTimeOffset CreateTime { get; init; }
}

public class PayPalVaultCardResult
{
    /// <summary>Vault payment token id - pass as payment_source.card.vault_id to pay with this card.</summary>
    public required string PaymentTokenId { get; init; }
    public required string CustomerId { get; init; }
    public string? CardBrand { get; init; }
    public string? LastDigits { get; init; }
    /// <summary>"YYYY-MM".</summary>
    public string? Expiry { get; init; }
}

public class PayPalTransactionRecord
{
    public required string TransactionId { get; init; }
    public string? PayPalReferenceId { get; init; }
    public required string EventCode { get; init; }
    public required string Status { get; init; }
    public decimal Amount { get; init; }
    public required string CurrencyCode { get; init; }
    public string? InvoiceId { get; init; }
    public DateTimeOffset InitiationDate { get; init; }
}

public class PayPalTransactionSearchResult
{
    public IReadOnlyList<PayPalTransactionRecord> Transactions { get; init; } = Array.Empty<PayPalTransactionRecord>();
    public int Page { get; init; }
    public int TotalPages { get; init; }
}
