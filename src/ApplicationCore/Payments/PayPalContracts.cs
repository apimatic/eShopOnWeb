using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public interface IPaymentSettings
{
    string Currency { get; }
}

public sealed record CardPaymentDetails
{
    public required string Number { get; init; }
    public required string Expiry { get; init; }
    public required string SecurityCode { get; init; }
    public string? Name { get; init; }
    public CardBillingAddress? BillingAddress { get; init; }
}

public sealed record CardBillingAddress
{
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? PostalCode { get; init; }
    public required string CountryCode { get; init; }
}

public sealed record PayPalAuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public required string AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public DateTimeOffset? Expiration { get; init; }
    public string? Amount { get; init; }
}

public sealed record PayPalCaptureResult
{
    public required string CaptureId { get; init; }
    public string? Status { get; init; }
    public required decimal CapturedAmount { get; init; }
    public decimal? PaypalFee { get; init; }
    public decimal? NetAmount { get; init; }
}

public sealed record PayPalAuthorizationSnapshot
{
    public required string AuthorizationId { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? Expiration { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
}

public sealed record PayPalRefundResult
{
    public required string RefundId { get; init; }
    public string? Status { get; init; }
    public required decimal Amount { get; init; }
}

public sealed record PayPalVaultedCardResult
{
    public required string VaultTokenId { get; init; }
    public string? PayPalCustomerId { get; init; }
    public string? LastDigits { get; init; }
    public string? Brand { get; init; }
    public string? Expiry { get; init; }
    public string? Name { get; init; }
}

public sealed record PayPalReportedTransaction
{
    public string? TransactionId { get; init; }
    public string? PaypalReferenceId { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public string? Status { get; init; }
    public string? Amount { get; init; }
    public string? FeeAmount { get; init; }
    public string? Currency { get; init; }
    public string? InitiationDate { get; init; }
    public string? PaymentMethodType { get; init; }
}

public interface IPayPalPaymentsGateway
{
    Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        string currency,
        CardPaymentDetails card,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationResult> AuthorizeVaultedCardAsync(
        int orderId,
        decimal amount,
        string currency,
        string vaultTokenId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationSnapshot> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationSnapshot> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        string invoiceId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<PayPalVaultedCardResult> VaultCardAsync(
        string merchantCustomerId,
        string? payPalCustomerId,
        CardPaymentDetails card,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayPalReportedTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
