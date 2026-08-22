using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeRequest request, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<PayPalVaultedCard> VaultCardAsync(PayPalVaultCardRequest request, CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListAllTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class PayPalAuthorizeRequest
{
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string CustomId { get; init; }
    public required string InvoiceId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required IReadOnlyList<PayPalOrderItem> Items { get; init; }
    public PayPalCardDetails? Card { get; init; }
    public string? VaultId { get; init; }
}

public sealed class PayPalOrderItem
{
    public required string Name { get; init; }
    public required int Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
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
    public string? AdminArea2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? PostalCode { get; init; }
    public required string CountryCode { get; init; }
}

public sealed class PayPalVaultCardRequest
{
    public required PayPalCardDetails Card { get; init; }
    public required string MerchantCustomerId { get; init; }
    public string? PayPalCustomerId { get; init; }
    public required string IdempotencyKey { get; init; }
}

public sealed class PayPalAuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public required string AuthorizationId { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
}

public sealed class PayPalCaptureResult
{
    public required string CaptureId { get; init; }
    public required string Status { get; init; }
    public required decimal CapturedAmount { get; init; }
    public required string Currency { get; init; }
    public decimal? PaypalFee { get; init; }
    public decimal? NetProceeds { get; init; }
}

public sealed class PayPalRefundResult
{
    public required string RefundId { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}

public sealed class PayPalVaultedCard
{
    public required string VaultId { get; init; }
    public required string LastDigits { get; init; }
    public required string Brand { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
    public string? PayPalCustomerId { get; init; }
}

public sealed class PayPalReportedTransaction
{
    public required string TransactionId { get; init; }
    public string? ReferenceId { get; init; }
    public string? CustomField { get; init; }
    public string? InvoiceId { get; init; }
    public string? EventCode { get; init; }
    public string? Status { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
    public DateTimeOffset? UpdatedDate { get; init; }
}
