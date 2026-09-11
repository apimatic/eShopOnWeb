using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Raw card details supplied for a one-off payment or to be vaulted. Never persisted.</summary>
public class GatewayCardDetails
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Expiry in YYYY-MM (PayPal date_year_month).</summary>
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public GatewayBillingAddress? BillingAddress { get; set; }
}

/// <summary>Portable billing address (maps to PayPal address fields).</summary>
public class GatewayBillingAddress
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    /// <summary>City.</summary>
    public string? AdminArea2 { get; set; }
    /// <summary>State / province.</summary>
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    /// <summary>ISO 3166-1 alpha-2 country code.</summary>
    public string? CountryCode { get; set; }
}

/// <summary>Request to authorize (hold) funds. Exactly one of Card / VaultId identifies the instrument.</summary>
public class GatewayAuthorizeRequest
{
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;

    /// <summary>Stable correlation value written to invoice_id/custom_id so PayPal txns line up with eShop orders.</summary>
    public string InvoiceId { get; set; } = string.Empty;

    /// <summary>Raw card for a one-off payment (mutually exclusive with VaultId).</summary>
    public GatewayCardDetails? Card { get; set; }

    /// <summary>Vault token id of a saved card (mutually exclusive with Card).</summary>
    public string? VaultId { get; set; }
}

public class GatewayAuthorizationResult
{
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string AuthorizationStatus { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
}

public class GatewayAuthorizationState
{
    public string AuthorizationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
}

public class GatewayCaptureResult
{
    public string CaptureId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
}

public class GatewayRefundResult
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
}

public class GatewayVaultResult
{
    public string VaultId { get; set; } = string.Empty;
    public string? PayPalCustomerId { get; set; }
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

/// <summary>A single transaction from PayPal's reporting API, projected to what reconciliation needs.</summary>
public class GatewayTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public string? InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string? CurrencyCode { get; set; }
    public string? Status { get; set; }
    public string? EventCode { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
}
