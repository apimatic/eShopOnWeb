using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalClient
{
    Task<PayPalAuthorizationResult> AuthorizeAsync(int orderId, string externalReference, decimal amount, string currency,
        PayPalPaymentSource paymentSource, string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<string> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency, string requestId,
        CancellationToken cancellationToken);
    Task<PayPalSavedCardResult> SaveCardAsync(PayPalCard card, string merchantCustomerId,
        string? paypalCustomerId, string requestId, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed record PayPalPaymentSource(PayPalCard? Card, string? VaultId)
{
    public static PayPalPaymentSource FromCard(PayPalCard card) => new(card, null);
    public static PayPalPaymentSource FromVault(string vaultId) => new(null, vaultId);
}

public sealed record PayPalCard(string Number, string Expiry, string SecurityCode, string Name,
    PayPalBillingAddress BillingAddress);

public sealed record PayPalBillingAddress(string AddressLine1, string? AddressLine2, string City, string State,
    string PostalCode, string CountryCode);

public sealed record PayPalAuthorizationResult(string PayPalOrderId, string AuthorizationId, string Status,
    decimal Amount, string Currency, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);

public sealed record PayPalCaptureResult(string CaptureId, string Status, decimal Amount, string Currency,
    decimal? PayPalFee, decimal? NetAmount, DateTimeOffset CreatedAt);

public sealed record PayPalRefundResult(string RefundId, string Status, decimal Amount, string Currency,
    DateTimeOffset CreatedAt);

public sealed record PayPalSavedCardResult(string VaultId, string CustomerId, string Brand, string LastFour,
    string Expiry);

public sealed record PayPalTransaction(string TransactionId, string? ReferenceId, string EventCode, string Status,
    decimal Amount, decimal Fee, string Currency, DateTimeOffset InitiatedAt, string? InvoiceId);

public class PayPalApiException : Exception
{
    public PayPalApiException(int statusCode, string code, string message, string? debugId = null,
        bool payerActionRequired = false) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        DebugId = debugId;
        PayerActionRequired = payerActionRequired;
    }

    public int StatusCode { get; }
    public string Code { get; }
    public string? DebugId { get; }
    public bool PayerActionRequired { get; }
}
