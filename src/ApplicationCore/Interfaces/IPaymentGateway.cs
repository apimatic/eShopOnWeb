using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentGateway
{
    string Currency { get; }

    Task<AuthorizationHold> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        CardPaymentSource card,
        string payPalRequestId,
        string? existingPayPalOrderId,
        CancellationToken cancellationToken);

    Task<AuthorizationHold> AuthorizeVaultedCardAsync(
        int orderId,
        decimal amount,
        string vaultId,
        string payPalRequestId,
        string? existingPayPalOrderId,
        CancellationToken cancellationToken);

    Task<AuthorizationHold> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    Task<AuthorizationHold> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<CaptureDetails> CaptureAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task VoidAsync(string authorizationId, string payPalRequestId, CancellationToken cancellationToken);

    Task<RefundDetails> RefundAsync(
        string captureId,
        decimal? amount,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    Task<VaultedCard> SaveCardAsync(
        string merchantCustomerId,
        string? payPalCustomerId,
        CardPaymentSource card,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken);
}

public sealed record CardPaymentSource(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

public sealed record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

public sealed record AuthorizationHold(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpirationTime,
    DateTimeOffset? CreateTime,
    string Currency);

public sealed record CaptureDetails(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PaypalFee,
    decimal? NetAmount,
    string Currency);

public sealed record RefundDetails(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public sealed record VaultedCard(
    string PaymentTokenId,
    string? LastDigits,
    string? Brand,
    string? Expiry,
    string? PayPalCustomerId,
    string? MerchantCustomerId);

public sealed record ProviderTransaction(
    string TransactionId,
    string? InvoiceId,
    string? CustomField,
    string? PaypalReferenceId,
    DateTimeOffset? InitiationDate,
    string? Amount,
    string? FeeAmount,
    string? Currency,
    string? Status,
    string? PaymentMethodType);
