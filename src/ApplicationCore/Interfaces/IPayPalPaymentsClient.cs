using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalPaymentsClient
{
    Task<PayPalAuthorizationResult> AuthorizeCardPaymentAsync(
        PayPalAuthorizeRequest request,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string invoiceId,
        string customId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCardResult> VaultCardAsync(
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

public sealed class PayPalAuthorizeRequest
{
    public required string InvoiceId { get; init; }
    public required string CustomId { get; init; }
    public required string Currency { get; init; }
    public required decimal Amount { get; init; }
    public required string RequestId { get; init; }
    public required IReadOnlyList<PayPalOrderItem> Items { get; init; }
    public PayPalShippingAddress? Shipping { get; init; }
    public PayPalCardDetails? Card { get; init; }
    public string? VaultId { get; init; }
}

public sealed class PayPalOrderItem
{
    public required string Name { get; init; }
    public required decimal UnitAmount { get; init; }
    public required int Quantity { get; init; }
    public string? Sku { get; init; }
}

public sealed class PayPalShippingAddress
{
    public string? FullName { get; init; }
    public required string AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public required string AdminArea2 { get; init; }
    public required string AdminArea1 { get; init; }
    public required string PostalCode { get; init; }
    public required string CountryCode { get; init; }
}

public sealed class PayPalCardDetails
{
    public required string Number { get; init; }
    public required string Expiry { get; init; }
    public required string SecurityCode { get; init; }
    public required string Name { get; init; }
    public PayPalShippingAddress? BillingAddress { get; init; }
}

public sealed class PayPalVaultCardRequest
{
    public required string RequestId { get; init; }
    public required PayPalCardDetails Card { get; init; }
    public string? MerchantCustomerId { get; init; }
}

public sealed class PayPalAuthorizationResult
{
    public required string PaypalOrderId { get; init; }
    public required string AuthorizationId { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public sealed class PayPalCaptureResult
{
    public required string CaptureId { get; init; }
    public required string Status { get; init; }
    public required decimal CapturedAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public decimal? PaypalFee { get; init; }
    public decimal? NetAmount { get; init; }
}

public sealed class PayPalRefundResult
{
    public required string RefundId { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public sealed class PayPalVaultedCardResult
{
    public required string VaultId { get; init; }
    public required string LastDigits { get; init; }
    public required string Brand { get; init; }
    public string? Expiry { get; init; }
    public string? Name { get; init; }
}

public sealed class PayPalReportedTransaction
{
    public required string TransactionId { get; init; }
    public string? ReferenceId { get; init; }
    public string? CustomField { get; init; }
    public string? InvoiceId { get; init; }
    public string? Status { get; init; }
    public string? EventCode { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
    public decimal? Amount { get; init; }
    public decimal? FeeAmount { get; init; }
    public string? Currency { get; init; }
}
