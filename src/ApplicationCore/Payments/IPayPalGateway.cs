using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public interface IPayPalGateway
{
    string Currency { get; }

    Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        string requestId,
        string invoiceId,
        string customId,
        decimal amount,
        IReadOnlyList<PayPalPurchaseItem> items,
        PayPalCardSource card,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> AuthorizeVaultedCardAsync(
        string requestId,
        string invoiceId,
        string customId,
        decimal amount,
        IReadOnlyList<PayPalPurchaseItem> items,
        string vaultId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string requestId,
        string authorizationId,
        decimal amount,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> CaptureAsync(
        string requestId,
        string authorizationId,
        string invoiceId,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(string requestId, string authorizationId, CancellationToken cancellationToken = default);

    Task<PayPalRefundResult> RefundAsync(
        string requestId,
        string captureId,
        decimal? amount,
        string currency,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCard> VaultCardAsync(
        string requestId,
        string merchantCustomerId,
        PayPalCardSource card,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public record PayPalPurchaseItem(string Name, decimal UnitAmount, int Quantity);

public record PayPalCardSource(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    PayPalBillingAddress? BillingAddress);

public record PayPalBillingAddress(
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode);

public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string PayPalOrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    decimal Amount,
    string Currency,
    DateTimeOffset? Expiration);

public record PayPalAuthorizationDetails(
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? Expiration,
    DateTimeOffset? CreateTime,
    string? CaptureId);

public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PaypalFee,
    decimal? NetAmount,
    string Currency);

public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public record PayPalVaultedCard(
    string PaymentTokenId,
    string? CustomerId,
    string LastDigits,
    string? Brand,
    string? Expiry,
    string? CardholderName);

public record PayPalReportedTransaction(
    string TransactionId,
    string? ReferenceId,
    string? InvoiceId,
    string? CustomField,
    string? Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? Time,
    string? EventCode);
