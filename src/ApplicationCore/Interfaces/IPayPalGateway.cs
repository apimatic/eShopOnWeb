using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    string Currency { get; }

    Task<PayPalAuthorizedOrder> AuthorizeCardPaymentAsync(
        PayPalAuthorizeRequest request,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorization> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorization> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalCapture> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalRefund> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCard> VaultCardAsync(
        PayPalCardDetails card,
        string requestId,
        CancellationToken cancellationToken = default);

    Task DeletePaymentTokenAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed class PayPalAuthorizeRequest
{
    public required string RequestId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string InvoiceId { get; init; }
    public required string CustomId { get; init; }
    public string? Description { get; init; }
    public PayPalCardDetails? Card { get; init; }
    public string? VaultId { get; init; }
}

public sealed class PayPalCardDetails
{
    public required string Number { get; init; }
    public required string Expiry { get; init; }
    public string? SecurityCode { get; init; }
    public string? Name { get; init; }
    public PayPalBillingAddress? BillingAddress { get; init; }
}

public sealed class PayPalBillingAddress
{
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? PostalCode { get; init; }
    public string? CountryCode { get; init; }
}

public sealed class PayPalAuthorizedOrder
{
    public required string OrderId { get; init; }
    public required string OrderStatus { get; init; }
    public required PayPalAuthorization Authorization { get; init; }
}

public sealed class PayPalAuthorization
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public decimal? Amount { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
}

public sealed class PayPalCapture
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
}

public sealed class PayPalRefund
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
}

public sealed class PayPalVaultedCard
{
    public required string PaymentTokenId { get; init; }
    public string? CustomerId { get; init; }
    public required string LastDigits { get; init; }
    public string? Brand { get; init; }
    public string? Expiry { get; init; }
    public string? Name { get; init; }
}

public sealed class PayPalReportedTransaction
{
    public string? TransactionId { get; init; }
    public string? PaypalReferenceId { get; init; }
    public string? PaypalReferenceIdType { get; init; }
    public string? TransactionEventCode { get; init; }
    public string? TransactionStatus { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public decimal? FeeAmount { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
    public DateTimeOffset? UpdatedDate { get; init; }
}
