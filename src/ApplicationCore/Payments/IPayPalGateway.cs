using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public interface IPayPalGateway
{
    Task<PayPalAuthorizationResult> AuthorizePaymentAsync(PayPalAuthorizeRequest request, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        string amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        string amount,
        string currency,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        string? amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCard> VaultCardAsync(PayPalVaultCardRequest request, CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed class PayPalCardSource
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

public sealed class PayPalAuthorizeRequest
{
    public required string Amount { get; init; }
    public required string Currency { get; init; }
    public required string InvoiceId { get; init; }
    public string? CustomId { get; init; }
    public required string CreateRequestId { get; init; }
    public required string AuthorizeRequestId { get; init; }
    public string? PayPalOrderId { get; init; }
    public PayPalCardSource? Card { get; init; }
    public string? VaultId { get; init; }
}

public sealed class PayPalAuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public required string AuthorizationId { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
    public string? Amount { get; init; }
}

public sealed class PayPalAuthorizationDetails
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
    public string? Amount { get; init; }
}

public sealed class PayPalCaptureResult
{
    public required string CaptureId { get; init; }
    public required string Status { get; init; }
    public required decimal CapturedAmount { get; init; }
    public decimal? PaypalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public required string Currency { get; init; }
}

public sealed class PayPalRefundResult
{
    public required string RefundId { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}

public sealed class PayPalVaultCardRequest
{
    public required PayPalCardSource Card { get; init; }
    public required string RequestId { get; init; }
    public string? PayPalCustomerId { get; init; }
}

public sealed class PayPalVaultedCard
{
    public required string PaymentTokenId { get; init; }
    public string? CustomerId { get; init; }
    public required string LastDigits { get; init; }
    public required string Brand { get; init; }
    public required string Expiry { get; init; }
    public string? CardholderName { get; init; }
}

public sealed class PayPalReportedTransaction
{
    public string? TransactionId { get; init; }
    public string? PaypalReferenceId { get; init; }
    public string? TransactionEventCode { get; init; }
    public string? TransactionStatus { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public string? Amount { get; init; }
    public string? Currency { get; init; }
    public string? FeeAmount { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
}

public sealed class PayPalGatewayException : Exception
{
    public PayPalGatewayException(int statusCode, string issue, string message, string? debugId = null)
        : base(message)
    {
        StatusCode = statusCode;
        Issue = issue;
        DebugId = debugId;
    }

    public int StatusCode { get; }
    public string Issue { get; }
    public string? DebugId { get; }
}
