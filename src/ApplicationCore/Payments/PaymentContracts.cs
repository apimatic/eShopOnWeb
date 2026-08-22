using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record BillingAddressInfo(
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode);

public sealed record CardPaymentSource(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    BillingAddressInfo? BillingAddress);

public sealed record VaultCardCommand(
    string ShopperId,
    string? PayPalCustomerId,
    CardPaymentSource Card);

public sealed record VaultedCardResult(
    string PaymentTokenId,
    string? PayPalCustomerId,
    string? LastDigits,
    string? Brand,
    string? Expiry,
    string? Name);

public sealed record AuthorizeCommand(
    string ShopperOrderId,
    string InvoiceId,
    decimal Amount,
    string Currency,
    CardPaymentSource? Card,
    string? VaultId);

public sealed record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    string AmountValue,
    string Currency,
    string? ExpirationTime,
    bool PayerActionRequired);

public sealed record AuthorizationSnapshot(
    string AuthorizationId,
    string Status,
    string? AmountValue,
    string? ExpirationTime);

public sealed record CaptureResult(
    string CaptureId,
    string Status,
    string? CapturedAmount,
    string? PaypalFee,
    string? NetAmount,
    string Currency);

public sealed record RefundResult(
    string RefundId,
    string Status,
    string AmountValue,
    string Currency);

public sealed record PayPalTransactionRecord(
    string? TransactionId,
    string? PaypalReferenceId,
    string? PaypalReferenceIdType,
    string? TransactionEventCode,
    string? TransactionInitiationDate,
    string? TransactionAmount,
    string? FeeAmount,
    string? TransactionStatus,
    string? InvoiceId,
    string? CustomField);

public static class MoneyFormat
{
    public static string ToValue(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static decimal Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0m;
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }
}

public sealed class PaymentSettings
{
    public string Currency { get; set; } = string.Empty;
}

public interface IPaymentSettings
{
    string Currency { get; }
}

public interface IPayPalGateway
{
    Task<VaultedCardResult> VaultCardAsync(VaultCardCommand command, string idempotencyKey, CancellationToken ct);
    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken ct);
    Task<AuthorizationResult> AuthorizePaymentAsync(AuthorizeCommand command, string createIdempotencyKey, string authorizeIdempotencyKey, CancellationToken ct);
    Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken ct);
    Task<AuthorizationSnapshot> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct);
    Task<CaptureResult> CaptureAsync(string authorizationId, string invoiceId, string idempotencyKey, CancellationToken ct);
    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct);
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct);
    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
