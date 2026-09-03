using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record ProviderAuthorization(
    string OrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record ProviderCapture(
    string CaptureId,
    string Status,
    decimal Amount,
    string Currency,
    decimal Fee,
    decimal Net);

public sealed record ProviderRefund(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt);

public sealed record ProviderPaymentMethod(
    string TokenId,
    string? CustomerId,
    string Brand,
    string Last4,
    string Expiry);

public interface IPaymentGateway
{
    string Currency { get; }

    Task<ProviderAuthorization> AuthorizeAsync(int orderId, string invoiceId, decimal amount, CardInput? card,
        string? vaultId, string requestId, CancellationToken cancellationToken);

    Task<ProviderAuthorization> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken);

    Task<ProviderAuthorization> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);

    Task<ProviderCapture> CaptureAsync(string authorizationId, string invoiceId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);

    Task<ProviderCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken);

    Task<string> VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken);

    Task<ProviderRefund> RefundAsync(string captureId, string invoiceId, int orderId, decimal? amount,
        string currency, string requestId, CancellationToken cancellationToken);

    Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken);

    Task<ProviderPaymentMethod> SavePaymentMethodAsync(string buyerId, CardInput card,
        string requestId, CancellationToken cancellationToken);

    Task DeletePaymentMethodAsync(string vaultId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}
