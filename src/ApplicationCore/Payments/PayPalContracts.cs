using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public static class PayPalMoney
{
    public static string Format(decimal amount, string currency)
    {
        var decimals = DecimalPlaces(currency);
        return Math.Round(amount, decimals, MidpointRounding.AwayFromZero)
            .ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    public static decimal Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    public static bool EqualsToTheCent(decimal left, decimal right, string currency)
    {
        var decimals = DecimalPlaces(currency);
        var scale = (decimal)Math.Pow(10, decimals);
        return Math.Round(left * scale, 0, MidpointRounding.AwayFromZero)
               == Math.Round(right * scale, 0, MidpointRounding.AwayFromZero);
    }

    public static int DecimalPlaces(string? currency)
    {
        return currency?.ToUpperInvariant() switch
        {
            "JPY" or "HUF" or "TWD" or "KRW" => 0,
            "BHD" or "JOD" or "KWD" or "OMR" or "TND" => 3,
            _ => 2
        };
    }
}

public sealed class CardPaymentSource
{
    public string Number { get; init; } = string.Empty;
    public string Expiry { get; init; } = string.Empty;
    public string SecurityCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public CardBillingAddress? BillingAddress { get; init; }
}

public sealed class CardBillingAddress
{
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? PostalCode { get; init; }
    public string CountryCode { get; init; } = "US";
}

public sealed class CreateAuthorizedPaymentRequest
{
    public string InvoiceId { get; init; } = string.Empty;
    public string CustomId { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Description { get; init; } = string.Empty;
    public CardPaymentSource? Card { get; init; }
    public string? VaultId { get; init; }
}

public sealed class PayPalOrderAuthorization
{
    public string OrderId { get; init; } = string.Empty;
    public string OrderStatus { get; init; } = string.Empty;
    public string AuthorizationId { get; init; } = string.Empty;
    public string AuthorizationStatus { get; init; } = string.Empty;
    public DateTimeOffset? CreateTime { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public sealed class PayPalAuthorizationDetails
{
    public string AuthorizationId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset? CreateTime { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public sealed class PayPalCaptureDetails
{
    public string CaptureId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal CapturedAmount { get; init; }
    public decimal PaypalFee { get; init; }
    public decimal NetProceeds { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public sealed class PayPalRefundDetails
{
    public string RefundId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public sealed class PayPalVaultedCard
{
    public string PaymentTokenId { get; init; } = string.Empty;
    public string? CustomerId { get; init; }
    public string? Brand { get; init; }
    public string? LastDigits { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
}

public sealed class PayPalReportedTransaction
{
    public string? TransactionId { get; init; }
    public string? ReferenceId { get; init; }
    public string? ReferenceIdType { get; init; }
    public string? EventCode { get; init; }
    public string? Status { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
    public string? AmountValue { get; init; }
    public string? AmountCurrency { get; init; }
    public string? FeeValue { get; init; }
    public string? FeeCurrency { get; init; }
}

public interface IPayPalGateway
{
    Task<PayPalOrderAuthorization> AuthorizeOrderAsync(
        CreateAuthorizedPaymentRequest request,
        string payPalRequestId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        string currency,
        decimal amount,
        string payPalRequestId,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureDetails> CaptureAuthorizationAsync(
        string authorizationId,
        string currency,
        decimal amount,
        string invoiceId,
        string payPalRequestId,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken = default);

    Task<PayPalRefundDetails> RefundCaptureAsync(
        string captureId,
        string currency,
        decimal? amount,
        string payPalRequestId,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCard> VaultCardAsync(
        CardPaymentSource card,
        string merchantCustomerId,
        string payPalRequestId,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListAllTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
