using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CardPaymentSource(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

public record AuthorizePaymentResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpirationTime,
    string Currency);

public record AuthorizationSnapshot(
    string AuthorizationId,
    string Status,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpirationTime,
    string? Amount,
    string? Currency);

public record CapturePaymentResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string Currency,
    DateTimeOffset? CapturedAt);

public record RefundPaymentResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public record VaultedCardResult(
    string PaymentTokenId,
    string? CustomerId,
    string LastDigits,
    string Brand,
    string Expiry,
    string? Name);

public record PayPalReportedTransaction(
    string TransactionId,
    string? Status,
    string? EventCode,
    string? InvoiceId,
    string? CustomField,
    string? ReferenceId,
    string? Amount,
    string? Currency,
    DateTimeOffset? InitiationDate);

public interface IPayPalGateway
{
    string Currency { get; }

    Task<AuthorizePaymentResult> AuthorizeCardAsync(
        decimal amount,
        string invoiceId,
        string customId,
        CardPaymentSource card,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<AuthorizePaymentResult> AuthorizeVaultedCardAsync(
        decimal amount,
        string invoiceId,
        string customId,
        string vaultId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<AuthorizationSnapshot> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<CapturePaymentResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    Task<RefundPaymentResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<VaultedCardResult> VaultCardAsync(
        CardPaymentSource card,
        string? paypalCustomerId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListAllTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
