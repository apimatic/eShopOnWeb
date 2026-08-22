using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed class PayPalAuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public string? PayPalOrderStatus { get; init; }
    public required string AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
    public decimal AuthorizedAmount { get; init; }
    public required string Currency { get; init; }
}

public sealed class PayPalAuthorizationDetails
{
    public required string AuthorizationId { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
    public decimal? Amount { get; init; }
}

public sealed class PayPalCaptureResult
{
    public required string CaptureId { get; init; }
    public string? Status { get; init; }
    public decimal CapturedAmount { get; init; }
    public decimal? PaypalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public required string Currency { get; init; }
}

public sealed class PayPalRefundResult
{
    public required string RefundId { get; init; }
    public string? Status { get; init; }
    public decimal Amount { get; init; }
    public decimal? TotalRefundedAmount { get; init; }
}

public sealed class PayPalVaultedCard
{
    public required string PaymentTokenId { get; init; }
    public string? PayPalCustomerId { get; init; }
    public string? MerchantCustomerId { get; init; }
    public string? LastDigits { get; init; }
    public string? Brand { get; init; }
    public string? Expiry { get; init; }
    public string? Name { get; init; }
}

public sealed class PayPalTransactionRecord
{
    public string? TransactionId { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public string? PaypalReferenceId { get; init; }
    public string? Status { get; init; }
    public string? Amount { get; init; }
    public string? FeeAmount { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
}
