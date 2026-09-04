using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record GatewayMoney(decimal Amount, string Currency);

public record GatewayAddress(
    string AddressLine1,
    string? AddressLine2,
    string? AdminArea1,
    string AdminArea2,
    string PostalCode,
    string CountryCode);

public record GatewayCard(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    GatewayAddress? BillingAddress);

public record GatewayAuthorizeRequest(
    GatewayMoney Amount,
    GatewayCard? Card,
    string? VaultTokenId);

public record GatewayAuthorizeResult(
    bool Success,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? Status,
    DateTimeOffset? ExpiresAt,
    GatewayMoney? Amount,
    string? DeclineReason,
    bool RequiresPayerAction);

public record GatewayCaptureResult(
    bool Success,
    string? CaptureId,
    string? Status,
    GatewayMoney? Amount,
    GatewayMoney? Fee,
    GatewayMoney? NetAmount,
    string? DeclineReason);

public record GatewayVoidResult(
    bool Success,
    string? Status,
    string? DeclineReason);

public record GatewayRefundResult(
    bool Success,
    string? RefundId,
    string? Status,
    GatewayMoney? Amount,
    string? DeclineReason);

public record GatewayVaultResult(
    bool Success,
    string? VaultId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? DeclineReason);

public record GatewayTransaction(
    string TransactionId,
    string Status,
    GatewayMoney Amount,
    DateTimeOffset InitiationDate,
    string? TransactionEventCode,
    string? InvoiceId);

/// <summary>
/// The boundary to PayPal. Implementations throw <see cref="Exceptions.PayPalGatewayException"/>
/// for transport/unreadable failures; business rejections (declines, payer action required)
/// come back as unsuccessful results.
/// </summary>
public interface IPayPalGateway
{
    Task<GatewayAuthorizeResult> AuthorizeAsync(GatewayAuthorizeRequest request, string idempotencyKey, CancellationToken ct);
    Task<GatewayAuthorizeResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct);
    Task<GatewayAuthorizeResult> ReauthorizeAsync(string authorizationId, GatewayMoney amount, string idempotencyKey, CancellationToken ct);
    Task<GatewayCaptureResult> CaptureAsync(string authorizationId, GatewayMoney amount, string idempotencyKey, CancellationToken ct);
    Task<GatewayVoidResult> VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);
    Task<GatewayRefundResult> RefundAsync(string captureId, GatewayMoney? amount, string idempotencyKey, CancellationToken ct);
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<GatewayVaultResult> SaveCardAsync(GatewayCard card, string merchantCustomerId, string idempotencyKey, CancellationToken ct);
    Task<GatewayVoidResult> DeleteCardAsync(string vaultId, CancellationToken ct);
}
