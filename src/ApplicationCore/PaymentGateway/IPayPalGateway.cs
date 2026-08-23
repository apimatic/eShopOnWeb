using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

public record MoneyAmount(decimal Value, string Currency);

public record CardDetails(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    BillingAddress? BillingAddress)
{
    public string LastFour => Number.Length >= 4 ? Number[^4..] : Number;
    public override string ToString() => $"Card ****{LastFour}";
}

public record BillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string AdminArea2,
    string AdminArea1,
    string PostalCode,
    string CountryCode);

public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? Expiration,
    MoneyAmount Amount);

public record AuthorizationDetails(
    string AuthorizationId,
    string Status,
    DateTimeOffset? Expiration,
    MoneyAmount Amount);

public record CaptureResult(
    string CaptureId,
    string Status,
    MoneyAmount Gross,
    MoneyAmount? Fee,
    MoneyAmount? Net);

public record RefundResult(
    string RefundId,
    string Status,
    MoneyAmount Amount);

public record VaultedCardResult(
    string PaymentTokenId,
    string? CustomerId,
    string LastDigits,
    string Brand,
    string Expiry,
    string? Name);

public record ReportedTransaction(
    string TransactionId,
    string? ReferenceId,
    string? CustomField,
    string? InvoiceId,
    string? EventCode,
    string? Status,
    MoneyAmount? Amount,
    MoneyAmount? Fee,
    DateTimeOffset? InitiationDate);

public interface IPayPalGateway
{
    string Currency { get; }

    Task<AuthorizationResult> AuthorizeCardAsync(
        string invoiceId,
        string customId,
        MoneyAmount amount,
        IReadOnlyList<PurchaseItem> items,
        CardDetails card,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<AuthorizationResult> AuthorizeVaultedCardAsync(
        string invoiceId,
        string customId,
        MoneyAmount amount,
        IReadOnlyList<PurchaseItem> items,
        string vaultId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<AuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<AuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        MoneyAmount amount,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<CaptureResult> CaptureAsync(
        string authorizationId,
        MoneyAmount amount,
        string requestId,
        CancellationToken cancellationToken = default);

    Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    Task<RefundResult> RefundAsync(
        string captureId,
        MoneyAmount amount,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<VaultedCardResult> VaultCardAsync(
        CardDetails card,
        string merchantCustomerId,
        string? paypalCustomerId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public record PurchaseItem(string Name, string Quantity, MoneyAmount UnitAmount);
