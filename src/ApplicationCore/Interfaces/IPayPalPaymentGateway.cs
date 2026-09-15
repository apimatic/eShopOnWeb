using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalPaymentGateway
{
    Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency, string invoiceId,
        string customId, string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string paypalOrderId, PayPalPaymentSource source,
        string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string invoiceId, string requestId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string customId, string requestId, CancellationToken cancellationToken);
    Task<PayPalVaultResult> VaultCardAsync(PayPalCard card, string merchantCustomerId,
        string setupRequestId, string tokenRequestId, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken);
    Task<PayPalTransactionPage> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        int page, int pageSize, CancellationToken cancellationToken);
}

public sealed record PayPalAddress(string AddressLine1, string? AddressLine2, string City, string? State,
    string PostalCode, string CountryCode);

public sealed record PayPalCard(string Number, string Expiry, string SecurityCode, string Name,
    PayPalAddress BillingAddress);

public sealed record PayPalPaymentSource(PayPalCard? Card, string? VaultId)
{
    public static PayPalPaymentSource OneOff(PayPalCard card) => new(card, null);
    public static PayPalPaymentSource Saved(string vaultId) => new(null, vaultId);
}

public sealed record PayPalOrderResult(string Id, string Status);
public sealed record PayPalAuthorizationResult(string Id, string Status, decimal Amount, string Currency,
    DateTimeOffset? CreateTime, DateTimeOffset? ExpirationTime, string? CardBrand, string? CardLast4,
    bool PayerActionRequired, string? OrderStatus = null);
public sealed record PayPalCaptureResult(string Id, string Status, decimal Amount, string Currency,
    decimal? PayPalFee, decimal? NetAmount, DateTimeOffset? CreateTime);
public sealed record PayPalRefundResult(string Id, string Status, decimal Amount, string Currency,
    DateTimeOffset? CreateTime);
public sealed record PayPalVaultResult(string Id, string? CustomerId, string Brand, string Last4, string Expiry);
public sealed record PayPalTransaction(string TransactionId, string? ReferenceId, string? ReferenceIdType,
    string? EventCode, DateTimeOffset? InitiatedAt, DateTimeOffset? UpdatedAt, decimal? Amount,
    decimal? Fee, string? Currency, string? Status, string? InvoiceId, string? CustomField);
public sealed record PayPalTransactionPage(IReadOnlyList<PayPalTransaction> Transactions, int Page,
    int? TotalPages, int? TotalItems);
