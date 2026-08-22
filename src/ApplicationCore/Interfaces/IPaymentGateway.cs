using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed class CardPaymentSource
{
    public string? Name { get; init; }
    public string? Number { get; init; }
    public string? Expiry { get; init; }
    public string? SecurityCode { get; init; }
    public CardBillingAddress? BillingAddress { get; init; }
    public string? VaultId { get; init; }
    public bool UseStoredCredential { get; init; }
}

public sealed class CardBillingAddress
{
    public string CountryCode { get; init; } = "US";
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? PostalCode { get; init; }
}

public sealed class AuthorizationHold
{
    public required string PayPalOrderId { get; init; }
    public required string AuthorizationId { get; init; }
    public required string Status { get; init; }
    public string? AmountValue { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
}

public sealed class AuthorizationSnapshot
{
    public required string AuthorizationId { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
    public string? AmountValue { get; init; }
    public string? Currency { get; init; }
}

public sealed class CaptureResult
{
    public required string CaptureId { get; init; }
    public required string Status { get; init; }
    public decimal? GrossAmount { get; init; }
    public decimal? PaypalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public string? Currency { get; init; }
}

public sealed class RefundGatewayResult
{
    public required string RefundId { get; init; }
    public required string Status { get; init; }
    public decimal Amount { get; init; }
    public decimal? TotalRefundedAmount { get; init; }
    public string? Currency { get; init; }
}

public sealed class VaultedCardResult
{
    public required string PaymentTokenId { get; init; }
    public string? PayPalCustomerId { get; init; }
    public string? LastDigits { get; init; }
    public string? Brand { get; init; }
    public string? Expiry { get; init; }
    public string? Name { get; init; }
}

public sealed class PayPalTransactionRecord
{
    public string? TransactionId { get; init; }
    public string? PaypalReferenceId { get; init; }
    public string? PaypalReferenceIdType { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public decimal? FeeAmount { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
    public DateTimeOffset? UpdatedDate { get; init; }
}

public interface IPaymentGateway
{
    Task<string> CreateOrderAsync(
        string invoiceId,
        string customId,
        decimal amount,
        string currency,
        string createRequestId,
        CancellationToken ct);

    Task<AuthorizationHold> AuthorizeExistingOrderAsync(
        string payPalOrderId,
        CardPaymentSource card,
        string authorizeRequestId,
        CancellationToken ct);

    Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    Task<AuthorizationHold> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken ct);

    Task<CaptureResult> CaptureAsync(
        string authorizationId,
        string requestId,
        string? invoiceId,
        CancellationToken ct);

    Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken ct);

    Task<string> VoidAsync(string authorizationId, string requestId, CancellationToken ct);

    Task<RefundGatewayResult> RefundAsync(
        string captureId,
        decimal? amount,
        string? currency,
        string requestId,
        CancellationToken ct);

    Task<VaultedCardResult> SaveCardAsync(
        string merchantCustomerId,
        string? payPalCustomerId,
        CardPaymentSource card,
        string requestId,
        CancellationToken ct);

    Task DeleteCardAsync(string paymentTokenId, CancellationToken ct);

    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct);
}

public interface IPaymentSettings
{
    string Currency { get; }
}
