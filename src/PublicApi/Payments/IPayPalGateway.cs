using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record SavedCardSource(string ProviderTokenId);
public sealed record CardSource(CardRequest Card);

public sealed record AuthorizationResult(string ProviderOrderId, string AuthorizationId, string Status,
    decimal Amount, DateTimeOffset? ExpiresAt);
public sealed record CaptureResult(string CaptureId, string Status, decimal Amount, decimal? Fee, decimal? Net);
public sealed record ReauthorizationResult(string AuthorizationId, string Status, decimal Amount, string Currency,
    DateTimeOffset? ExpiresAt);
public sealed record VoidResult(string Status);
public sealed record ProviderRefundResult(string RefundId, string Status, decimal Amount);
public sealed record VaultResult(string TokenId, string? CustomerId, string Brand, string Last4, string? Expiry);
public sealed record ProviderTransaction(string? TransactionId, string? ReferenceId, string? EventCode,
    DateTimeOffset? InitiatedAt, decimal? Amount, decimal? Fee, string? Currency, string? Status,
    string? InvoiceId);

public interface IPayPalGateway
{
    Task<AuthorizationResult> AuthorizeAsync(int orderId, string paymentReference, decimal total, string currency,
        object paymentSource, CancellationToken cancellationToken);
    Task<ReauthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
    Task<ReauthorizationResult> ReauthorizeAsync(int orderId, string paymentReference, string authorizationId,
        decimal total, string currency, CancellationToken cancellationToken);
    Task<CaptureResult> CaptureAsync(int orderId, string paymentReference, string authorizationId, decimal total,
        string currency, CancellationToken cancellationToken);
    Task<VoidResult> VoidAsync(int orderId, string paymentReference, string authorizationId,
        CancellationToken cancellationToken);
    Task<ProviderRefundResult> RefundAsync(int orderId, string paymentReference, string captureId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);
    Task<ProviderRefundResult> GetRefundAsync(string refundId, decimal expectedAmount, string currency,
        CancellationToken cancellationToken);
    Task<VaultResult> SaveCardAsync(string paymentReference, string buyerId, CardRequest card,
        CancellationToken cancellationToken);
    Task DeleteCardAsync(string providerTokenId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        string currency, CancellationToken cancellationToken);
}
