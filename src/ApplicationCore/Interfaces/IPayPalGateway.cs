using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    Task<PayPalCheckoutOrder> CreateAuthorizedCardOrderAsync(
        PayPalAuthorizeOrderRequest request,
        string payPalRequestId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        string currencyCode,
        string amount,
        string payPalRequestId,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureDetails> CaptureAuthorizationAsync(
        string authorizationId,
        string currencyCode,
        string amount,
        string invoiceId,
        string payPalRequestId,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureDetails> GetCaptureAsync(
        string captureId,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken = default);

    Task<PayPalRefundDetails> RefundCaptureAsync(
        string captureId,
        string? currencyCode,
        string? amount,
        string payPalRequestId,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCard> VaultCardAsync(
        PayPalCardDetails card,
        string merchantCustomerId,
        string payPalRequestId,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed class PayPalCardDetails
{
    public required string Name { get; init; }
    public required string Number { get; init; }
    public required string Expiry { get; init; }
    public required string SecurityCode { get; init; }
    public required PayPalBillingAddress BillingAddress { get; init; }
}

public sealed class PayPalBillingAddress
{
    public required string AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public required string AdminArea2 { get; init; }
    public required string AdminArea1 { get; init; }
    public required string PostalCode { get; init; }
    public required string CountryCode { get; init; }
}

public sealed class PayPalAuthorizeOrderRequest
{
    public required string CurrencyCode { get; init; }
    public required string Amount { get; init; }
    public required string CustomId { get; init; }
    public required string InvoiceId { get; init; }
    public required string Description { get; init; }
    public PayPalCardDetails? Card { get; init; }
    public string? VaultId { get; init; }
    public PayPalBillingAddress? ShippingAddress { get; init; }
}

public sealed class PayPalCheckoutOrder
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public PayPalAuthorizationDetails? Authorization { get; init; }
    public string? PayerActionUrl { get; init; }
}

public sealed class PayPalAuthorizationDetails
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public string? Amount { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
}

public sealed class PayPalCaptureDetails
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public decimal? CapturedAmount { get; init; }
    public decimal? PaypalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public string? Currency { get; init; }
}

public sealed class PayPalRefundDetails
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public decimal Amount { get; init; }
    public string? Currency { get; init; }
}

public sealed class PayPalVaultedCard
{
    public required string PaymentTokenId { get; init; }
    public string? CustomerId { get; init; }
    public string? LastDigits { get; init; }
    public string? Brand { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
}

public sealed class PayPalReportedTransaction
{
    public required string TransactionId { get; init; }
    public string? Status { get; init; }
    public string? EventCode { get; init; }
    public string? Amount { get; init; }
    public string? FeeAmount { get; init; }
    public string? Currency { get; init; }
    public string? CustomField { get; init; }
    public string? InvoiceId { get; init; }
    public string? ReferenceId { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
}
