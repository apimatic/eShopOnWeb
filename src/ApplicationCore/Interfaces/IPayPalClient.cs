using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Card details used for a one-off payment or for vaulting. Instances must
/// never be persisted or logged.
/// </summary>
public class CardDetails
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // YYYY-MM
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public CardBillingAddress? BillingAddress { get; set; }
}

public class CardBillingAddress
{
    public string? AddressLine1 { get; set; }
    public string? AdminArea2 { get; set; } // city
    public string? AdminArea1 { get; set; } // state
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}

public class PayPalOrderResult
{
    public string OrderId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// For direct card payments PayPal authorizes inline during order creation;
    /// when present, no separate authorize call is needed.
    /// </summary>
    public PayPalAuthorizationResult? Authorization { get; set; }
}

public class PayPalAuthorizationResult
{
    public string AuthorizationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? ExpirationTime { get; set; }
}

public class PayPalCaptureResult
{
    public string CaptureId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
}

public class PayPalRefundResult
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
}

public class PayPalSetupTokenResult
{
    public string SetupTokenId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
}

public class PayPalVaultedCard
{
    public string VaultTokenId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class PayPalTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? FeeAmount { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public DateTimeOffset? UpdatedDate { get; set; }
}

/// <summary>
/// Abstraction over the PayPal REST API (Orders v2, Payments v2, Vault v3,
/// Transaction Search v1). Implemented in Infrastructure.
/// </summary>
public interface IPayPalClient
{
    Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency, CardDetails card, string invoiceId, string requestId, CancellationToken cancellationToken = default);
    Task<PayPalOrderResult> CreateOrderWithVaultedCardAsync(decimal amount, string currency, string vaultTokenId, string invoiceId, string requestId, CancellationToken cancellationToken = default);
    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string payPalOrderId, string requestId, CancellationToken cancellationToken = default);
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);
    Task<PayPalAuthorizationResult> ReauthorizeAuthorizationAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default);
    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<PayPalSetupTokenResult> CreateSetupTokenAsync(CardDetails card, string? payPalCustomerId, string requestId, CancellationToken cancellationToken = default);
    Task<PayPalVaultedCard> CreatePaymentTokenAsync(string setupTokenId, string requestId, CancellationToken cancellationToken = default);
    Task DeletePaymentTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
