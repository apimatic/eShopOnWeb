using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's view of PayPal. All PayPal SDK contact happens behind this interface (implemented in
/// Infrastructure); no SDK type crosses this boundary, so the domain and API layers depend only on these
/// plain records. Card details flow in but are never returned or persisted.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>Create a PayPal order for the total and authorize (hold) the funds with a card or saved vault id.</summary>
    Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeRequest request, CancellationToken cancellationToken);

    /// <summary>Capture (take) an authorized payment in full.</summary>
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);

    /// <summary>Renew a stale authorization so a capture can proceed.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);

    /// <summary>Void an authorization, releasing the held funds.</summary>
    Task VoidAsync(string authorizationId, CancellationToken cancellationToken);

    /// <summary>Refund a captured payment, fully (null amount) or partially.</summary>
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string requestId, CancellationToken cancellationToken);

    /// <summary>Vault (save) a card and return its token + safe descriptor.</summary>
    Task<PayPalVaultResult> VaultCardAsync(PayPalVaultCardRequest request, CancellationToken cancellationToken);

    /// <summary>Remove a vaulted card so it can no longer fund a payment.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken);

    /// <summary>List PayPal's own transaction records over a date range (all pages, whole range).</summary>
    Task<PayPalTransactionSearchResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}

/// <summary>Raw card details supplied by a shopper for a one-off payment or to vault. Transient — never stored or logged.</summary>
public sealed record CardDetails
{
    public required string Number { get; init; }
    public required string Expiry { get; init; }   // YYYY-MM
    public string? SecurityCode { get; init; }
    public string? CardHolderName { get; init; }
    public string? BillingStreet { get; init; }
    public string? BillingCity { get; init; }
    public string? BillingState { get; init; }
    public string? BillingCountryCode { get; init; }
    public string? BillingPostalCode { get; init; }
}

public sealed record PayPalAuthorizeRequest
{
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string InvoiceId { get; init; }
    public required string CustomId { get; init; }
    /// <summary>Deterministic per-order idempotency key (PayPal-Request-Id).</summary>
    public required string RequestId { get; init; }
    public string? Description { get; init; }
    /// <summary>One-off card details, OR set <see cref="VaultId"/> for a saved card. Exactly one is used.</summary>
    public CardDetails? Card { get; init; }
    public string? VaultId { get; init; }
}

public sealed record PayPalAuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? CardBrand { get; init; }
    public string? CardLastDigits { get; init; }
    /// <summary>True when PayPal requires a browser approval (challenge/3DS) instead of authorizing directly.</summary>
    public bool RequiresApproval { get; init; }
    public string? ApprovalMessage { get; init; }
}

public sealed record PayPalCaptureResult
{
    public required string CaptureId { get; init; }
    public string? Status { get; init; }
    public required decimal CapturedAmount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public required string Currency { get; init; }
}

public sealed record PayPalRefundResult
{
    public required string RefundId { get; init; }
    public string? Status { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}

public sealed record PayPalVaultCardRequest
{
    public required CardDetails Card { get; init; }
    /// <summary>Existing PayPal customer id for this shopper, if one was created earlier.</summary>
    public string? PayPalCustomerId { get; init; }
    /// <summary>The shopper's stable id in our system, associated with the PayPal customer.</summary>
    public required string MerchantCustomerId { get; init; }
    public string? RequestId { get; init; }
}

public sealed record PayPalVaultResult
{
    public required string VaultId { get; init; }
    public string? PayPalCustomerId { get; init; }
    public string? Brand { get; init; }
    public string? LastDigits { get; init; }
    public string? Expiry { get; init; }
    public string? CardHolderName { get; init; }
}

/// <summary>One PayPal transaction record from the reporting API.</summary>
public sealed record PayPalTransaction
{
    public string? TransactionId { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public decimal? FeeAmount { get; init; }
    public string? Status { get; init; }
    public string? EventCode { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
}

/// <summary>All transactions PayPal reported over the requested range, plus how many pages were walked.</summary>
public sealed record PayPalTransactionSearchResult
{
    public required IReadOnlyList<PayPalTransaction> Transactions { get; init; }
    public required int PagesRetrieved { get; init; }
    public required int WindowsQueried { get; init; }
    /// <summary>Always false — the whole range is walked; present so callers see coverage was complete.</summary>
    public bool Truncated { get; init; }
}
