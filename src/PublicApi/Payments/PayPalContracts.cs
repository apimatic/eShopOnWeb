using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record CardInput(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    BillingAddressInput? BillingAddress);

public sealed record BillingAddressInput(
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? Region,
    string? PostalCode);

public sealed record PayPalAuthorizationResult(
    string PayPalOrderId,
    string? OrderStatus,
    string? AuthorizationId,
    string? AuthorizationStatus,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt,
    bool PayerActionRequired);

public sealed record PayPalCaptureResult(
    string Id,
    string? Status,
    decimal Amount,
    string Currency,
    decimal? Fee,
    decimal? Net,
    DateTimeOffset? CreatedAt);

public sealed record PayPalRefundResult(
    string Id,
    string? Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? UpdatedAt);

public sealed record PayPalVaultResult(
    string Id,
    string? Name,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? Type);

public sealed record PayPalTransaction(
    string? TransactionId,
    string? ReferenceId,
    string? EventCode,
    string? Status,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? InvoiceId,
    string? CustomId);

public interface IPayPalGateway
{
    Task<PayPalAuthorizationResult> AuthorizeAsync(string orderReference, decimal amount, string currency,
        string createRequestId, string authorizeRequestId, CardInput? card, string? vaultId,
        CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task<string?> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken);
    Task<PayPalRefundResult> GetRefundAsync(string refundId, CancellationToken cancellationToken);
    Task<PayPalVaultResult> SaveCardAsync(string buyerId, CardInput card, string requestId,
        CancellationToken cancellationToken);
    Task DeleteSavedCardAsync(string vaultId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed class PayPalProviderException : Exception
{
    public PayPalProviderException(string message, string? providerName = null, string? debugId = null,
        Exception? innerException = null) : base(message, innerException)
    {
        ProviderName = providerName;
        DebugId = debugId;
    }

    public string? ProviderName { get; }
    public string? DebugId { get; }
}
