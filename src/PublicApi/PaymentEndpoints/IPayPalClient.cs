using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed record PayPalCard(string Number, string Expiry, string SecurityCode, string Name,
    string CountryCode, string? AddressLine1, string? AddressLine2, string? AdminArea1,
    string? AdminArea2, string? PostalCode);

public sealed record PayPalAuthorizeCommand(string CorrelationId, int OrderId, decimal Amount,
    string Currency, string InvoiceId, PayPalCard? Card, string? VaultId);

public sealed record PayPalAuthorizationResult(string OrderId, string AuthorizationId, string Status,
    decimal Amount, string Currency, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt,
    DateTimeOffset? ExpirationTime);

public sealed record PayPalCaptureResult(string CaptureId, string Status, decimal Amount, string Currency,
    decimal? Fee, decimal? NetAmount, DateTimeOffset? CreatedAt);

public sealed record PayPalRefundResult(string RefundId, string Status, decimal Amount, string Currency,
    DateTimeOffset CreatedAt);

public sealed record PayPalSavedCardResult(string PaymentTokenId, string? CustomerId, string Brand,
    string Last4, string Expiry);

public sealed record PayPalTransactionResult(string TransactionId, string? ReferenceId, string? InvoiceId,
    string? CustomField, string? EventCode, string? Status, decimal? Amount, decimal? Fee,
    string? Currency, DateTimeOffset? InitiationDate);

public interface IPayPalClient
{
    Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeCommand command, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencySeed, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string invoiceId, string idempotencySeed, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task VoidAsync(string authorizationId, string idempotencySeed, CancellationToken cancellationToken);
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency, string? note,
        string idempotencySeed, CancellationToken cancellationToken);
    Task<PayPalSavedCardResult> SaveCardAsync(PayPalCard card, string merchantCustomerId,
        string idempotencySeed, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayPalTransactionResult>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}
