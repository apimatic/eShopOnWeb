using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed class CardDetails
{
    public CardDetails(
        string number,
        string expiry,
        string securityCode,
        string name,
        CardBillingAddress billingAddress)
    {
        Number = NormalizeCardNumber(number);
        Expiry = expiry?.Trim() ?? string.Empty;
        SecurityCode = securityCode?.Trim() ?? string.Empty;
        Name = name?.Trim() ?? string.Empty;
        BillingAddress = billingAddress;
    }

    private static string NormalizeCardNumber(string number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return string.Empty;
        }

        var buffer = new System.Text.StringBuilder(number.Length);
        foreach (var ch in number)
        {
            if (char.IsDigit(ch))
            {
                buffer.Append(ch);
            }
        }

        return buffer.ToString();
    }

    public string Number { get; }
    public string Expiry { get; }
    public string SecurityCode { get; }
    public string Name { get; }
    public CardBillingAddress BillingAddress { get; }

    public string LastDigits => Number.Length >= 4 ? Number[^4..] : Number;

    public override string ToString() => $"{Name} ****{LastDigits}";
}

public sealed class CardBillingAddress
{
    public CardBillingAddress(
        string addressLine1,
        string? adminArea2,
        string? adminArea1,
        string postalCode,
        string countryCode)
    {
        AddressLine1 = addressLine1;
        AdminArea2 = adminArea2;
        AdminArea1 = adminArea1;
        PostalCode = postalCode;
        CountryCode = countryCode;
    }

    public string AddressLine1 { get; }
    public string? AdminArea2 { get; }
    public string? AdminArea1 { get; }
    public string PostalCode { get; }
    public string CountryCode { get; }
}

public sealed class AuthorizePaymentCommand
{
    public AuthorizePaymentCommand(
        int orderId,
        decimal amount,
        string currency,
        CardDetails? card,
        string? vaultId,
        string idempotencyKey,
        string invoiceId)
    {
        OrderId = orderId;
        Amount = amount;
        Currency = currency;
        Card = card;
        VaultId = vaultId;
        IdempotencyKey = idempotencyKey;
        InvoiceId = invoiceId;
    }

    public int OrderId { get; }
    public decimal Amount { get; }
    public string Currency { get; }
    public CardDetails? Card { get; }
    public string? VaultId { get; }
    public string IdempotencyKey { get; }
    public string InvoiceId { get; }
}

public sealed class PaymentAuthorizationResult
{
    public PaymentAuthorizationResult(
        string payPalOrderId,
        string? payPalOrderStatus,
        string authorizationId,
        string? authorizationStatus,
        DateTimeOffset? expiration,
        decimal authorizedAmount,
        string currency)
    {
        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = payPalOrderStatus;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        Expiration = expiration;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
    }

    public string PayPalOrderId { get; }
    public string? PayPalOrderStatus { get; }
    public string AuthorizationId { get; }
    public string? AuthorizationStatus { get; }
    public DateTimeOffset? Expiration { get; }
    public decimal AuthorizedAmount { get; }
    public string Currency { get; }
}

public sealed class PaymentCaptureResult
{
    public PaymentCaptureResult(
        string captureId,
        string? captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount,
        string currency,
        string? authorizationId,
        string? authorizationStatus)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        Currency = currency;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
    }

    public string CaptureId { get; }
    public string? CaptureStatus { get; }
    public decimal CapturedAmount { get; }
    public decimal? PaypalFee { get; }
    public decimal? NetAmount { get; }
    public string Currency { get; }
    public string? AuthorizationId { get; }
    public string? AuthorizationStatus { get; }
}

public sealed class PaymentRefundResult
{
    public PaymentRefundResult(string refundId, string? status, decimal amount, string currency)
    {
        RefundId = refundId;
        Status = status;
        Amount = amount;
        Currency = currency;
    }

    public string RefundId { get; }
    public string? Status { get; }
    public decimal Amount { get; }
    public string Currency { get; }
}

public sealed class VaultedCardResult
{
    public VaultedCardResult(
        string paymentTokenId,
        string? customerId,
        string brand,
        string lastDigits,
        string? expiry,
        string? cardholderName)
    {
        PaymentTokenId = paymentTokenId;
        CustomerId = customerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
    }

    public string PaymentTokenId { get; }
    public string? CustomerId { get; }
    public string Brand { get; }
    public string LastDigits { get; }
    public string? Expiry { get; }
    public string? CardholderName { get; }
}

public sealed class GatewayTransaction
{
    public GatewayTransaction(
        string transactionId,
        string? referenceId,
        string? invoiceId,
        string? customField,
        string? eventCode,
        string? status,
        decimal? amount,
        string? currency,
        DateTimeOffset? initiationDate)
    {
        TransactionId = transactionId;
        ReferenceId = referenceId;
        InvoiceId = invoiceId;
        CustomField = customField;
        EventCode = eventCode;
        Status = status;
        Amount = amount;
        Currency = currency;
        InitiationDate = initiationDate;
    }

    public string TransactionId { get; }
    public string? ReferenceId { get; }
    public string? InvoiceId { get; }
    public string? CustomField { get; }
    public string? EventCode { get; }
    public string? Status { get; }
    public decimal? Amount { get; }
    public string? Currency { get; }
    public DateTimeOffset? InitiationDate { get; }
}

public sealed class AuthorizationSnapshot
{
    public AuthorizationSnapshot(
        string authorizationId,
        string? status,
        DateTimeOffset? expiration,
        decimal? amount,
        string? currency)
    {
        AuthorizationId = authorizationId;
        Status = status;
        Expiration = expiration;
        Amount = amount;
        Currency = currency;
    }

    public string AuthorizationId { get; }
    public string? Status { get; }
    public DateTimeOffset? Expiration { get; }
    public decimal? Amount { get; }
    public string? Currency { get; }

    public bool IsExpired(DateTimeOffset utcNow) =>
        string.Equals(Status, "EXPIRED", StringComparison.OrdinalIgnoreCase)
        || (Expiration.HasValue && Expiration.Value <= utcNow);
}

public interface IPaymentSettings
{
    string Currency { get; }
}

public interface IPaymentGateway
{
    Task<PaymentAuthorizationResult> AuthorizeAsync(AuthorizePaymentCommand command, CancellationToken cancellationToken = default);

    Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<AuthorizationSnapshot> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<PaymentCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<PaymentRefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<VaultedCardResult> VaultCardAsync(string merchantCustomerId, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
