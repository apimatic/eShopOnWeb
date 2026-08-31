using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Card details used for a one-off payment or for vaulting. Full card data passes through
/// to PayPal only; it is never persisted by this application and never written to logs.
/// </summary>
public class CardPaymentDetails
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // YYYY-MM
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public CardBillingAddress? BillingAddress { get; set; }
}

public class CardBillingAddress
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; } // city
    public string? AdminArea1 { get; set; } // state
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}

public class PayPalAuthorizationResult
{
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
}

public class PayPalCaptureResult
{
    public string CaptureId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class PayPalRefundResult
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class PayPalSetupTokenResult
{
    public string SetupTokenId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? CustomerId { get; set; }
}

public class PayPalPaymentTokenResult
{
    public string PaymentTokenId { get; set; } = string.Empty;
    public string? CustomerId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string? Expiry { get; set; }
}

/// <summary>One transaction as reported by PayPal's Transaction Search API.</summary>
public class PayPalTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public DateTimeOffset? UpdatedDate { get; set; }
    public string? ReferenceId { get; set; }
}

/// <summary>
/// The PayPal side of the integration: Orders v2 authorize/capture, Payments v2
/// capture/void/reauthorize/refund, Payment Method Tokens v3 vault, Transaction Search v1.
/// Implemented over plain HTTPS against the PayPal REST API.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>Creates a PayPal order (intent=AUTHORIZE) paid with raw card details and authorizes it.</summary>
    Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardPaymentDetails card,
        string referenceId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Creates a PayPal order (intent=AUTHORIZE) paid with a vaulted card and authorizes it.</summary>
    Task<PayPalAuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultPaymentTokenId,
        string referenceId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Captures an authorized payment; this is when the money actually moves.</summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization. Returns a new authorization id and honor period.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default);

    /// <summary>Releases the held funds without any money moving.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment, in part (amount given) or in full (amount null).</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string? noteToPayer, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card: creates a setup token and exchanges it for a reusable payment token.</summary>
    Task<PayPalPaymentTokenResult> VaultCardAsync(CardPaymentDetails card, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Removes a vaulted card from PayPal's vault.</summary>
    Task DeletePaymentTokenAsync(string vaultPaymentTokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own record of transactions for a date range, paging through the
    /// whole range rather than just the first page.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
