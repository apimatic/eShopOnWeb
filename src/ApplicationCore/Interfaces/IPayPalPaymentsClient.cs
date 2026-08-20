using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed class CardPaymentDetails
{
    public string Number { get; init; } = string.Empty;
    public string Expiry { get; init; } = string.Empty;
    public string? SecurityCode { get; init; }
    public string? Name { get; init; }
    public CardBillingAddress? BillingAddress { get; init; }
}

public sealed class CardBillingAddress
{
    public string CountryCode { get; init; } = string.Empty;
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? PostalCode { get; init; }
}

public sealed class PayPalMoney
{
    public string CurrencyCode { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

public sealed class PayPalPurchaseItem
{
    public string Name { get; init; } = string.Empty;
    public string Quantity { get; init; } = string.Empty;
    public PayPalMoney UnitAmount { get; init; } = new();
    public string? Sku { get; init; }
    public string Category { get; init; } = "PHYSICAL_GOODS";
}

public sealed class PayPalAuthorizationResult
{
    public string PayPalOrderId { get; init; } = string.Empty;
    public string OrderStatus { get; init; } = string.Empty;
    public string AuthorizationId { get; init; } = string.Empty;
    public string AuthorizationStatus { get; init; } = string.Empty;
    public DateTimeOffset? ExpirationTime { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
    public PayPalMoney? Amount { get; init; }
    public string? InvoiceId { get; init; }
}

public sealed class PayPalCaptureResult
{
    public string CaptureId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal CapturedAmount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public sealed class PayPalRefundResult
{
    public string RefundId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public sealed class PayPalVaultedCard
{
    public string PaymentTokenId { get; init; } = string.Empty;
    public string? LastDigits { get; init; }
    public string? Brand { get; init; }
    public string? Expiry { get; init; }
    public string? Name { get; init; }
}

public sealed class PayPalReportedTransaction
{
    public string TransactionId { get; init; } = string.Empty;
    public string? ReferenceId { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public string? EventCode { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
    public string? AmountValue { get; init; }
    public string? AmountCurrency { get; init; }
    public string? FeeValue { get; init; }
}

public interface IPayPalPaymentsClient
{
    string Currency { get; }

    string FormatMoney(decimal amount);

    decimal ParseMoney(string? value);

    Task<PayPalAuthorizationResult> AuthorizeCardPaymentAsync(
        int orderId,
        decimal amount,
        IReadOnlyList<PayPalPurchaseItem> items,
        CardPaymentDetails card,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> AuthorizeVaultedCardPaymentAsync(
        int orderId,
        decimal amount,
        IReadOnlyList<PayPalPurchaseItem> items,
        string vaultId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult?> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        bool finalCapture,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCard> VaultCardAsync(
        string merchantCustomerId,
        CardPaymentDetails card,
        string requestId,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
