using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The payment processor, as eShop sees it. Every method either returns what the processor reported or
/// throws <see cref="Payments.PaymentGatewayException"/>.
/// </summary>
public interface IPaymentGateway
{
    Task<GatewayOrder> CreateOrderAsync(CreateGatewayOrder request, CancellationToken cancellationToken);

    /// <summary>Authorizes (holds) the order total using either the card or the vaulted token in <paramref name="source"/>.</summary>
    Task<GatewayOrderAuthorization> AuthorizeOrderAsync(string gatewayOrderId, PaymentSourceInput source, string requestId, CancellationToken cancellationToken);

    /// <summary>Re-reads an order to learn whether (and how) it was authorized.</summary>
    Task<GatewayOrderAuthorization> GetOrderAsync(string gatewayOrderId, CancellationToken cancellationToken);

    Task<GatewayAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken);

    Task<GatewayCapture> CaptureAsync(string authorizationId, decimal amount, string currency, string? invoiceId, string requestId, CancellationToken cancellationToken);

    Task<GatewayAuthorization> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);

    Task<GatewayRefund> RefundAsync(string captureId, decimal amount, string currency, string customId, string requestId, CancellationToken cancellationToken);

    Task<GatewayVaultedCard> VaultCardAsync(CardDetails card, string? customerId, string requestId, CancellationToken cancellationToken);

    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken);

    /// <summary>False when the processor reports the token does not exist.</summary>
    Task<bool> VaultedCardExistsAsync(string vaultId, CancellationToken cancellationToken);

    /// <summary>All vault ids PayPal holds for a customer; <c>Complete</c> is false when the listing was capped.</summary>
    Task<(IReadOnlyList<string> VaultIds, bool Complete)> ListVaultedCardsAsync(string customerId, CancellationToken cancellationToken);

    /// <summary>One page of the processor's transaction report. The range must not exceed 31 days.</summary>
    Task<GatewayTransactionPage> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, int page, int pageSize, CancellationToken cancellationToken);
}

public record CreateGatewayOrder(decimal Amount, string Currency, string CustomId, string InvoiceId, string Description, string RequestId);

public record GatewayOrder(string Id, string? Status);

public record GatewayOrderAuthorization(
    string OrderId,
    string? OrderStatus,
    GatewayAuthorization? Authorization,
    string? CardBrand,
    string? CardLastDigits);

public record GatewayAuthorization(
    string Id,
    string? Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt);

public record GatewayCapture(string Id, string? Status, decimal Amount, string Currency, decimal? Fee, decimal? Net);

public record GatewayRefund(string Id, string? Status, decimal? Amount);

public record GatewayVaultedCard(string VaultId, string? CustomerId, string? Brand, string? LastDigits, string? Expiry, string? CardholderName);

public record GatewayTransaction(
    string TransactionId,
    string? EventCode,
    string? Status,
    DateTimeOffset? Date,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? InvoiceId,
    string? CustomField,
    string? ReferenceId);

public record GatewayTransactionPage(IReadOnlyList<GatewayTransaction> Transactions, int Page, int TotalPages);

/// <summary>Exactly one of <see cref="Card"/> or <see cref="VaultId"/> is set.</summary>
public sealed class PaymentSourceInput
{
    private PaymentSourceInput(CardDetails? card, string? vaultId)
    {
        Card = card;
        VaultId = vaultId;
    }

    public CardDetails? Card { get; }
    public string? VaultId { get; }

    public static PaymentSourceInput FromCard(CardDetails card) => new(card ?? throw new ArgumentNullException(nameof(card)), null);
    public static PaymentSourceInput FromVault(string vaultId) => new(null, string.IsNullOrWhiteSpace(vaultId) ? throw new ArgumentException("Vault id required.", nameof(vaultId)) : vaultId);

    public override string ToString() => VaultId is null ? "card" : "vaulted card";
}

/// <summary>
/// Raw card data on its way to the processor. Never persisted and never logged:
/// <see cref="ToString"/> is overridden so an accidental log line cannot print it.
/// </summary>
public sealed class CardDetails
{
    public CardDetails(string number, string expiry, string? securityCode, string? name, CardBillingAddress? billingAddress)
    {
        Number = number;
        Expiry = expiry;
        SecurityCode = securityCode;
        Name = name;
        BillingAddress = billingAddress;
    }

    public string Number { get; }

    /// <summary>YYYY-MM.</summary>
    public string Expiry { get; }
    public string? SecurityCode { get; }
    public string? Name { get; }
    public CardBillingAddress? BillingAddress { get; }

    public override string ToString() => "[card details redacted]";
}

public record CardBillingAddress(string? AddressLine1, string? AddressLine2, string? City, string? State, string? PostalCode, string CountryCode);
