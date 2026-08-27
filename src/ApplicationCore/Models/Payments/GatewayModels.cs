using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>The payment source for an authorization: either full card details or a vaulted card token.</summary>
public class GatewayPaymentSource
{
    public GatewayCard? Card { get; set; }
    public string? VaultTokenId { get; set; }

    public static GatewayPaymentSource FromCard(GatewayCard card) => new() { Card = card };
    public static GatewayPaymentSource FromVaultToken(string vaultTokenId) => new() { VaultTokenId = vaultTokenId };
}

public class GatewayOrder
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class GatewayAuthorization
{
    public string AuthorizationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateTimeOffset? ExpiryTime { get; set; }

    /// <summary>Safe display details of the card used, when the provider reports them.</summary>
    public string? CardBrand { get; set; }
    public string? CardLastDigits { get; set; }
}

public class GatewayCapture
{
    public string CaptureId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal? Fee { get; set; }
    public decimal? NetAmount { get; set; }
}

public class GatewayRefund
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
}

/// <summary>A vaulted card as the provider describes it — safe display details only.</summary>
public class GatewaySavedCard
{
    public string VaultTokenId { get; set; } = string.Empty;
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
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal? FeeAmount { get; set; }
    public DateTimeOffset? InitiationTime { get; set; }
    public DateTimeOffset? UpdatedTime { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
}

public class GatewayTransactionPage
{
    public IReadOnlyList<GatewayTransaction> Transactions { get; set; } = Array.Empty<GatewayTransaction>();
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
}
