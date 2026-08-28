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
    CardAddressInput BillingAddress);

public sealed record CardAddressInput(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? State,
    string PostalCode,
    string CountryCode);

public sealed record ProviderOrder(string Id, string? Status);

public sealed record ProviderAuthorization(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    string? OrderStatus,
    string? CreateTime,
    string? UpdateTime,
    string? ExpirationTime);

public sealed record ProviderCapture(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    decimal GrossAmount,
    decimal? Fee,
    decimal? NetAmount,
    string? CreateTime,
    string? UpdateTime);

public sealed record ProviderRefund(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    string? CreateTime,
    string? UpdateTime);

public sealed record ProviderPaymentMethod(
    string Id,
    string CustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardType);

public sealed record ProviderTransaction(
    string? TransactionId,
    string? ReferenceId,
    string? EventCode,
    string? Status,
    string? InitiatedAt,
    string? UpdatedAt,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? InvoiceId,
    string? CustomField);

public interface IPayPalGateway
{
    Task<ProviderOrder> CreateOrderAsync(string amount, string currency, string invoiceId,
        string customId, string requestId, CancellationToken cancellationToken);
    Task<ProviderAuthorization> AuthorizeAsync(string payPalOrderId, CardInput? card,
        string? vaultId, string requestId, CancellationToken cancellationToken);
    Task<ProviderAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
    Task<ProviderAuthorization> ReauthorizeAsync(string authorizationId, string amount,
        string currency, string requestId, CancellationToken cancellationToken);
    Task<ProviderCapture> CaptureAsync(string authorizationId, string amount,
        string currency, string requestId, CancellationToken cancellationToken);
    Task<ProviderCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task<ProviderAuthorization> VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken);
    Task<ProviderRefund> RefundAsync(string captureId, string? amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken);
    Task<ProviderPaymentMethod> SaveCardAsync(string merchantCustomerId, string? providerCustomerId,
        CardInput card, string requestId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderPaymentMethod>> ListCardsAsync(string customerId,
        CancellationToken cancellationToken);
    Task<ProviderPaymentMethod> GetCardAsync(string tokenId, CancellationToken cancellationToken);
    Task DeleteCardAsync(string tokenId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}
