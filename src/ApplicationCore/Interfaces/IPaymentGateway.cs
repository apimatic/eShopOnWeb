using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Card details used for a one-off payment or for vaulting. Full card data must
/// flow straight through to the payment processor: it is never persisted and
/// never written to logs.
/// </summary>
public class CardDetails
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Card expiry in ISO-8601 YYYY-MM format.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public CardBillingAddress? BillingAddress { get; set; }
}

public class CardBillingAddress
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;
}

public class GatewayOrder
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    /// <summary>
    /// When the order is created with a card payment source, the processor authorizes
    /// it in the same call and returns the resulting authorization here.
    /// </summary>
    public GatewayAuthorization? Authorization { get; set; }
    public string? CardBrand { get; set; }
    public string? CardLastDigits { get; set; }
}

public class GatewayAuthorization
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? ExpirationTime { get; set; }
}

public class GatewayAuthorizeResult
{
    public string OrderId { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
    public GatewayAuthorization? Authorization { get; set; }
    public string? CardBrand { get; set; }
    public string? CardLastDigits { get; set; }
}

public class GatewayCapture
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
}

public class GatewayRefund
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class GatewayVaultToken
{
    public string Id { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class GatewayTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? ReferenceIdType { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? FeeAmount { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomId { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
}

/// <summary>
/// Abstraction over the payment processor (PayPal). Implementations are built
/// against the processor's OpenAPI contract; all operations that move money
/// accept a caller-supplied idempotency key.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Creates a processor order for the given amount, optionally with a card or vaulted card as payment source.</summary>
    Task<GatewayOrder> CreateOrderAsync(decimal amount, string currency, string customId, string invoiceId,
        CardDetails? card, string? vaultTokenId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order amount. Returns the resulting authorization.</summary>
    Task<GatewayAuthorizeResult> AuthorizeOrderAsync(string gatewayOrderId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<GatewayAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization. Throws if the processor can no longer renew it.</summary>
    Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes) the full authorized amount. When invoiceId is null, the authorization's invoice id is reported.</summary>
    Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, string? invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Releases a hold without any money moving.</summary>
    Task<GatewayAuthorization> VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment; null amount refunds the remaining captured amount in full.</summary>
    Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currency, string? noteToPayer, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card for the given processor customer id and returns the token plus safe card metadata.</summary>
    Task<GatewayVaultToken> CreateVaultTokenAsync(CardDetails card, string customerId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task DeleteVaultTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>Lists the processor's own record of transactions over [from, to], covering every page of the range.</summary>
    Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
