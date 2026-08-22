using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed class CardPaymentInput
{
    public required string Number { get; init; }
    public required string Expiry { get; init; }
    public required string SecurityCode { get; init; }
    public string? Name { get; init; }
    public CardBillingAddress? BillingAddress { get; init; }
}

public sealed class CardBillingAddress
{
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? PostalCode { get; init; }
    public required string CountryCode { get; init; }
}

public sealed class AuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public string? PayPalOrderStatus { get; init; }
    public required string AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
}

public sealed class CaptureResult
{
    public required string CaptureId { get; init; }
    public string? CaptureStatus { get; init; }
    public required decimal CapturedAmount { get; init; }
    public decimal? PaypalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public string? AuthorizationStatus { get; init; }
}

public sealed class AuthorizationDetails
{
    public required string AuthorizationId { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
    public decimal? Amount { get; init; }
}

public sealed class CaptureDetails
{
    public required string CaptureId { get; init; }
    public string? Status { get; init; }
    public required decimal CapturedAmount { get; init; }
    public decimal? PaypalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public decimal? RefundedAmount { get; init; }
}

public sealed class RefundResult
{
    public required string RefundId { get; init; }
    public string? Status { get; init; }
    public required decimal Amount { get; init; }
    public string? CaptureStatus { get; init; }
}

public sealed class VaultedCardResult
{
    public required string PaymentTokenId { get; init; }
    public string? PayPalCustomerId { get; init; }
    public string? LastDigits { get; init; }
    public string? Brand { get; init; }
    public string? Expiry { get; init; }
}

public sealed class ProviderTransaction
{
    public string? TransactionId { get; init; }
    public string? PaypalReferenceId { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public string? Status { get; init; }
    public string? Amount { get; init; }
    public string? Currency { get; init; }
    public string? FeeAmount { get; init; }
    public string? InitiationDate { get; init; }
}

public interface IPaymentProcessor
{
    Task<AuthorizationResult> AuthorizeCardAsync(int orderId, decimal amount, string currency, CardPaymentInput card, string requestId, CancellationToken ct);
    Task<AuthorizationResult> AuthorizeVaultedCardAsync(int orderId, decimal amount, string currency, string vaultId, string requestId, CancellationToken ct);
    Task<AuthorizationResult> AuthorizeExistingPayPalOrderAsync(string paypalOrderId, string requestId, CancellationToken ct);
    Task<AuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken ct);
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken ct);
    Task<CaptureResult> CaptureAsync(string authorizationId, string requestId, CancellationToken ct);
    Task<CaptureDetails> GetCaptureAsync(string captureId, CancellationToken ct);
    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken ct);
    Task<RefundResult> RefundAsync(string captureId, string? paypalOrderId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct);
    Task<VaultedCardResult> VaultCardAsync(string merchantCustomerId, string? paypalCustomerId, CardPaymentInput card, string requestId, CancellationToken ct);
    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken ct);
    Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
