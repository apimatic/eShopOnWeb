using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

public class VaultedCardResult
{
    public required string VaultId { get; init; }
    public string? CardBrand { get; init; }
    public string? Last4 { get; init; }
    public string? Expiry { get; init; }
}

public class OrderAuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public required string AuthorizationId { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public class ReauthorizationResult
{
    public required string AuthorizationId { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public class CaptureResult
{
    public required string CaptureId { get; init; }
    public required string Status { get; init; }
    public required decimal CapturedAmount { get; init; }
    public decimal? FeeAmount { get; init; }
    public decimal? NetAmount { get; init; }
}

public class RefundResult
{
    public required string RefundId { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
    public decimal? TotalRefundedAmount { get; init; }
}

public class PayPalTransactionRecord
{
    public required string TransactionId { get; init; }
    public string? PayPalOrderId { get; init; }
    public decimal? Amount { get; init; }
    public string? CurrencyCode { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? InitiatedAt { get; init; }
}
