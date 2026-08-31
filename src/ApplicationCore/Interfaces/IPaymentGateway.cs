using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Card details used to authorize a payment or vault a card.
/// Instances of this type must never be persisted or logged.
/// </summary>
public class CardDetails
{
    public string Number { get; set; } = string.Empty;
    public string ExpiryMonth { get; set; } = string.Empty;
    public string ExpiryYear { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
    public string? BillingAddressLine1 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }

    public string ExpiryForGateway() => $"{ExpiryYear}-{ExpiryMonth.PadLeft(2, '0')}";
}

public class GatewayAuthorizationResult
{
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
}

public class GatewayCaptureResult
{
    public string CaptureId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Fee { get; set; }
    public decimal NetAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class GatewayRefundResult
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class GatewayVaultResult
{
    public string VaultTokenId { get; set; } = string.Empty;
    public string? PayPalCustomerId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;
    public string ExpiryMonth { get; set; } = string.Empty;
    public string ExpiryYear { get; set; } = string.Empty;
}

public class GatewayTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? ReferenceIdType { get; set; }
    public string? EventCode { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal? Fee { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset InitiatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Abstraction over the payment processor (PayPal). Implementations live in Infrastructure.
/// </summary>
public interface IPaymentGateway
{
    Task<GatewayAuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<GatewayAuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultTokenId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<GatewayCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<GatewayAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, CancellationToken cancellationToken = default);
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);
    Task<GatewayRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<GatewayVaultResult> VaultCardAsync(CardDetails card, string? payPalCustomerId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
