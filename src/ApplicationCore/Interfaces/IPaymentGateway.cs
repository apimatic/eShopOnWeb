using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Card details for a one-off payment or for vaulting. Full card data passes through to the
/// payment provider only — it is never persisted and never logged.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    BillingAddressDetails? BillingAddress);

public record BillingAddressDetails(
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);

public record AuthorizePaymentRequest(
    int LocalOrderId,
    decimal Amount,
    string Currency,
    CardDetails? Card,
    string? VaultPaymentTokenId,
    string? ExistingPayPalOrderId,
    string CreateRequestKey,
    string AuthorizeRequestKey);

public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    decimal Amount,
    string Currency,
    string? DeclineReason);

public record GatewayAuthorizationStatus(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    decimal? Amount);

public record CaptureResult(
    string CaptureId,
    string Status,
    decimal Amount,
    decimal? Fee,
    decimal? NetAmount,
    string Currency);

public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    decimal? TotalRefunded,
    string Currency);

/// <summary>
/// Money movement against the payment provider: authorize (hold), capture (take), reauthorize
/// (renew a stale hold), void (release a hold), refund (give back). Every write takes a stable
/// idempotency key the provider de-duplicates on.
/// </summary>
public interface IPaymentGateway
{
    Task<AuthorizationResult> AuthorizeAsync(AuthorizePaymentRequest request, CancellationToken ct);
    Task<GatewayAuthorizationStatus> GetAuthorizationAsync(string authorizationId, CancellationToken ct);
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestKey, CancellationToken ct);
    Task<CaptureResult> CaptureAsync(string authorizationId, string requestKey, CancellationToken ct);
    Task VoidAsync(string authorizationId, string requestKey, CancellationToken ct);
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string requestKey, string? noteToPayer, CancellationToken ct);
}
