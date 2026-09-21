using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's sole seam to PayPal. Implemented over the PayPal Server SDK in Infrastructure;
/// translates SDK exceptions into <see cref="Exceptions.PayPalException"/>. Raw card details flow
/// through the request records here and are never persisted or logged.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>Create an AUTHORIZE-intent order for the given funding source and place a hold.</summary>
    Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeRequest request, CancellationToken ct);

    /// <summary>Re-read an order's authorization (unknown-outcome recovery after a transport failure).</summary>
    Task<PayPalAuthorizationInfo?> GetOrderAuthorizationAsync(string payPalOrderId, CancellationToken ct);

    /// <summary>Current state of an authorization.</summary>
    Task<PayPalAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Renew a stale authorization; returns the new authorization to capture against.</summary>
    Task<PayPalAuthorizationInfo> ReauthorizeAsync(string authorizationId, string requestId, CancellationToken ct);

    /// <summary>Capture (take) an authorized payment in full.</summary>
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string requestId, CancellationToken ct);

    /// <summary>Void (release) an authorized payment before capture.</summary>
    Task VoidAsync(string authorizationId, string requestId, CancellationToken ct);

    /// <summary>Refund a captured payment, in full (null amount) or in part.</summary>
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode,
        string requestId, CancellationToken ct);

    /// <summary>Vault a card for later reuse; returns the token id and a safe descriptor.</summary>
    Task<PayPalVaultResult> VaultCardAsync(PayPalVaultCardRequest request, CancellationToken ct);

    /// <summary>Remove a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultTokenAsync(string vaultId, CancellationToken ct);

    /// <summary>PayPal's own transactions over a date range, covering every page.</summary>
    Task<PayPalTransactionSearchResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct);
}

/// <summary>Raw card details for a one-off payment or a vault request. Transient; never stored.</summary>
public record CardDetails(
    string Number,
    string Expiry,          // YYYY-MM
    string SecurityCode,
    string? CardholderName,
    string? BillingLine1,
    string? BillingCity,
    string? BillingState,
    string? BillingCountryCode,
    string? BillingPostalCode);

public record PayPalAuthorizeRequest(
    decimal Amount,
    string CurrencyCode,
    string InvoiceId,
    string OrderReference,     // custom_id tying the PayPal order back to the eShop order
    string? Description,
    CardDetails? Card,         // one-off card ...
    string? VaultId,           // ... or a saved-card token (exactly one is set)
    string CreateRequestId,
    string AuthorizeRequestId);

public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string AuthorizationStatus,
    string OrderStatus,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? TransactionTime);

public record PayPalAuthorizationInfo(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    string CurrencyCode,
    decimal? PayPalFee,
    decimal? NetAmount,
    DateTimeOffset? TransactionTime);

public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal? Amount);

public record PayPalVaultCardRequest(
    string BuyerId,
    CardDetails Card);

public record PayPalVaultResult(
    string VaultId,
    string? CustomerId,
    string? Brand,
    string? Last4,
    string? Expiry);

public record PayPalTransaction(
    string? TransactionId,
    string? InvoiceId,
    string? CustomField,
    decimal? Amount,
    string? CurrencyCode,
    decimal? FeeAmount,
    DateTimeOffset? InitiationTime,
    string? Status);

/// <summary>Result of a reconciliation search over PayPal transactions.</summary>
public record PayPalTransactionSearchResult(
    IReadOnlyList<PayPalTransaction> Transactions,
    int PagesFetched,
    int TotalItems,
    bool Truncated);
