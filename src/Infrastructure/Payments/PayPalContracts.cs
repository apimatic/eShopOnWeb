using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed record PayPalAddress(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

public sealed record PayPalCardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string Name,
    PayPalAddress BillingAddress);

public sealed record PayPalAuthorization(
    string PayPalOrderId,
    string PayPalOrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    decimal Amount,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt,
    string? CardBrand,
    string? CardLastDigits);

public sealed record PayPalCapture(
    string Id,
    string Status,
    decimal Amount,
    decimal? Fee,
    decimal? NetAmount,
    DateTimeOffset? CreatedAt);

public sealed record PayPalRefund(
    string Id,
    string Status,
    decimal Amount);

public sealed record PayPalSavedCard(
    string TokenId,
    string CustomerId,
    string Brand,
    string LastDigits,
    string Expiry,
    string? Name);

public sealed record PayPalTransaction(
    string Id,
    string? ReferenceId,
    string? ReferenceType,
    string? InvoiceId,
    string EventCode,
    string Status,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt,
    decimal Amount,
    string Currency,
    decimal? Fee);

public interface IPayPalClient
{
    Task<PayPalAuthorization> AuthorizeOrderAsync(
        int orderId,
        string integrationId,
        string invoiceId,
        decimal amount,
        string currency,
        PayPalCardDetails? card,
        string? vaultId,
        CancellationToken cancellationToken);

    Task<PayPalAuthorization> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken);

    Task<PayPalCapture> CaptureAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken);

    Task<PayPalCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken);

    Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);

    Task<PayPalRefund> RefundAsync(
        string captureId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken);

    Task<PayPalSavedCard> SaveCardAsync(
        string merchantCustomerId,
        string? payPalCustomerId,
        PayPalCardDetails card,
        CancellationToken cancellationToken);

    Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayPalTransaction>> GetTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
