using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment provider (PayPal). All monetary values are decimal;
/// statuses are the provider's wire values (e.g. "CREATED", "COMPLETED").
/// Full card details pass through here only — they are never persisted or logged.
/// </summary>
public interface IPaymentGateway
{
    string Currency { get; }

    Task<GatewayOrderResult> CreateOrderAsync(string idempotencyKey, decimal amount, string currency,
        string customId, CancellationToken ct);

    Task<GatewayAuthorizationResult> AuthorizeWithCardAsync(string payPalOrderId, string idempotencyKey,
        CardPaymentDetails card, CancellationToken ct);

    Task<GatewayAuthorizationResult> AuthorizeWithVaultedCardAsync(string payPalOrderId, string idempotencyKey,
        string vaultTokenId, CancellationToken ct);

    Task<GatewayAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    Task<GatewayAuthorizationInfo> ReauthorizeAsync(string authorizationId, string idempotencyKey,
        decimal amount, string currency, CancellationToken ct);

    Task<GatewayCaptureResult> CaptureAsync(string authorizationId, string idempotencyKey,
        decimal amount, string currency, string invoiceId, CancellationToken ct);

    Task<GatewayAuthorizationInfo> VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    Task<GatewayRefundResult> RefundAsync(string captureId, string idempotencyKey,
        decimal? amount, string currency, string customId, CancellationToken ct);

    Task<GatewayVaultedCard> VaultCardAsync(string idempotencyKey, string shopperKey,
        CardPaymentDetails card, CancellationToken ct);

    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct);

    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct);
}

public class CardPaymentDetails
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // YYYY-MM
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public Address? BillingAddress { get; set; }
}

public class GatewayOrderResult
{
    public string PayPalOrderId { get; set; } = string.Empty;
    public string? Status { get; set; }
}

public class GatewayAuthorizationResult
{
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? StatusReason { get; set; }
    public DateTimeOffset? ExpirationTime { get; set; }
}

public class GatewayAuthorizationInfo
{
    public string AuthorizationId { get; set; } = string.Empty;
    public string? Status { get; set; }
    public DateTimeOffset? ExpirationTime { get; set; }
}

public class GatewayCaptureResult
{
    public string CaptureId { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? StatusReason { get; set; }
    public decimal? GrossAmount { get; set; }
    public decimal? SellerFee { get; set; }
    public decimal? NetAmount { get; set; }
}

public class GatewayRefundResult
{
    public string RefundId { get; set; } = string.Empty;
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
}

public class GatewayVaultedCard
{
    public string VaultTokenId { get; set; } = string.Empty;
    public string? PayPalCustomerId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class GatewayTransaction
{
    public string? TransactionId { get; set; }
    public string? ReferenceId { get; set; }
    public string? ReferenceIdType { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? EventCode { get; set; }
    public DateTimeOffset? Time { get; set; }
}
