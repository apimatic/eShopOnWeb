using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record CardInput(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    CardBillingAddressInput BillingAddress);

public sealed record CardBillingAddressInput(
    string AddressLine1,
    string? AddressLine2,
    string AdminArea2,
    string? AdminArea1,
    string PostalCode,
    string CountryCode);

public sealed record PayPalOrderResult(string Id, string Status);

public sealed record AuthorizationResult(
    string PayPalOrderStatus,
    bool PayerActionRequired,
    string? Id,
    string Status,
    string? StatusReason,
    decimal Amount,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record CaptureResult(
    string Id,
    string Status,
    string? StatusReason,
    decimal Amount,
    decimal? PayPalFee,
    decimal? NetAmount,
    DateTimeOffset? CreatedAt);

public sealed record RefundResult(
    string Id,
    string Status,
    string? StatusReason,
    decimal Amount,
    DateTimeOffset? UpdatedAt);

public sealed record VaultedCardResult(
    string TokenId,
    string CustomerId,
    string? Name,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? Type);

public sealed record PayPalTransactionResult(
    string? TransactionId,
    string? PayPalReferenceId,
    string? ReferenceType,
    string? EventCode,
    DateTimeOffset? InitiationDate,
    DateTimeOffset? UpdatedDate,
    decimal? Amount,
    decimal? Fee,
    string? Currency,
    string? Status,
    string? InvoiceId,
    string? CustomField,
    string? PaymentInstrumentType);

public interface IPayPalPaymentGateway
{
    string Currency { get; }
    Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string invoiceId, string requestId, CancellationToken ct);
    Task<AuthorizationResult> AuthorizeAsync(string payPalOrderId, decimal amount, CardInput? card,
        string? vaultedTokenId, string requestId, CancellationToken ct);
    Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct);
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string requestId, CancellationToken ct);
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string requestId, CancellationToken ct);
    Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken ct);
    Task<AuthorizationResult> VoidAsync(string authorizationId, string requestId, CancellationToken ct);
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string requestId, CancellationToken ct);
    Task<RefundResult> GetRefundAsync(string refundId, CancellationToken ct);
    Task<VaultedCardResult> SaveCardAsync(string merchantCustomerId, CardInput card, string requestId, CancellationToken ct);
    Task<IReadOnlySet<string>> ListVaultedTokenIdsAsync(string customerId, CancellationToken ct);
    Task DeleteVaultedTokenAsync(string tokenId, CancellationToken ct);
    Task<IReadOnlyList<PayPalTransactionResult>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
