using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CardPaymentDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode);

public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? Expiration,
    string Currency,
    string AuthorizedValue);

public record CaptureResult(
    string CaptureId,
    string Status,
    decimal Amount,
    decimal? PaypalFee,
    decimal? NetAmount,
    string Currency);

public record RefundResult(
    string PayPalRefundId,
    string Status,
    decimal Amount,
    string Currency);

public record VaultedCardResult(
    string PayPalVaultId,
    string LastDigits,
    string Brand,
    string? Expiry,
    string? CardholderName);

public record ProviderTransaction(
    string TransactionId,
    string? InvoiceId,
    string? CustomField,
    string? ReferenceId,
    string? Status,
    string? Amount,
    string? Fee,
    string? Currency,
    string? InitiationDate);

public interface IPaymentGateway
{
    Task<AuthorizationResult> AuthorizeAsync(
        int orderId,
        string invoiceId,
        decimal amount,
        string currency,
        CardPaymentDetails? card,
        string? vaultId,
        CancellationToken cancellationToken);

    Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    Task<AuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken);

    Task<CaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken);

    Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);

    Task<string> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);

    Task<RefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<VaultedCardResult> VaultCardAsync(
        string merchantCustomerId,
        CardPaymentDetails card,
        CancellationToken cancellationToken);

    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
