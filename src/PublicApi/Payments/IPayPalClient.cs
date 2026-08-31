using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record PayPalCard(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode);

public sealed record PayPalAuthorization(
    string OrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record PayPalCapture(
    string CaptureId,
    string Status,
    decimal Amount,
    decimal? PayPalFee,
    decimal? NetAmount,
    DateTimeOffset? CreatedAt);

public sealed record PayPalRefund(
    string RefundId,
    string Status,
    decimal Amount,
    DateTimeOffset? CreatedAt);

public sealed record PayPalVaultedCard(
    string VaultId,
    string Brand,
    string Last4,
    string Expiry);

public sealed record PayPalTransaction(
    string TransactionId,
    string? PayPalReferenceId,
    string? EventCode,
    string? Status,
    DateTimeOffset? InitiatedAt,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? InvoiceId,
    string? CustomField);

public interface IPayPalClient
{
    string Currency { get; }
    Task<PayPalAuthorization> AuthorizeAsync(int orderId, string paymentReference, decimal amount,
        PayPalCard? card, string? vaultId, string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount,
        string requestId, CancellationToken cancellationToken);
    Task<PayPalCapture> CaptureAsync(string authorizationId, decimal amount,
        string requestId, CancellationToken cancellationToken);
    Task<PayPalCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task<string> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<PayPalRefund> RefundAsync(string captureId, decimal amount,
        string requestId, CancellationToken cancellationToken);
    Task<PayPalVaultedCard> VaultCardAsync(string buyerId, PayPalCard card,
        string requestId, CancellationToken cancellationToken);
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}
