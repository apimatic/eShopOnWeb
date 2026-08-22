using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    Task<PayPalOrderAuthorization> AuthorizeCardPaymentAsync(
        PayPalCardAuthorizationRequest request,
        CancellationToken cancellationToken = default);

    Task<PayPalOrderAuthorization> AuthorizeVaultedCardPaymentAsync(
        PayPalVaultAuthorizationRequest request,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureDetails> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalRefundDetails> RefundCaptureAsync(
        string captureId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCard> VaultCardAsync(
        PayPalVaultCardRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(
        string vaultId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed class PayPalCardAuthorizationRequest
{
    public required string RequestId { get; init; }
    public required string InvoiceId { get; init; }
    public required string CustomId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string CardNumber { get; init; }
    public required string Expiry { get; init; }
    public required string SecurityCode { get; init; }
    public required string CardholderName { get; init; }
    public required PayPalBillingAddress BillingAddress { get; init; }
}

public sealed class PayPalVaultAuthorizationRequest
{
    public required string RequestId { get; init; }
    public required string InvoiceId { get; init; }
    public required string CustomId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string VaultId { get; init; }
}

public sealed class PayPalVaultCardRequest
{
    public required string RequestId { get; init; }
    public required string MerchantCustomerId { get; init; }
    public required string CardNumber { get; init; }
    public required string Expiry { get; init; }
    public required string SecurityCode { get; init; }
    public required string CardholderName { get; init; }
    public required PayPalBillingAddress BillingAddress { get; init; }
}

public sealed class PayPalBillingAddress
{
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? PostalCode { get; init; }
    public required string CountryCode { get; init; }
}

public sealed class PayPalOrderAuthorization
{
    public required string PayPalOrderId { get; init; }
    public required string PayPalOrderStatus { get; init; }
    public required string AuthorizationId { get; init; }
    public required string AuthorizationStatus { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed class PayPalAuthorizationDetails
{
    public required string AuthorizationId { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed class PayPalCaptureDetails
{
    public required string CaptureId { get; init; }
    public required string Status { get; init; }
    public required decimal CapturedAmount { get; init; }
    public decimal? PaypalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public required string Currency { get; init; }
}

public sealed class PayPalRefundDetails
{
    public required string RefundId { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}

public sealed class PayPalVaultedCard
{
    public required string VaultId { get; init; }
    public string? LastDigits { get; init; }
    public string? Brand { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
}

public sealed class PayPalReportedTransaction
{
    public required string TransactionId { get; init; }
    public string? ReferenceId { get; init; }
    public string? EventCode { get; init; }
    public string? Status { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public decimal? Amount { get; init; }
    public decimal? FeeAmount { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
}
