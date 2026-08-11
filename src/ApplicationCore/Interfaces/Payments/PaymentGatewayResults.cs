using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>Result of creating + authorizing (or re-authorizing) an order at PayPal.</summary>
public class AuthorizationResult
{
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>Result of capturing an authorization, carrying what PayPal reported.</summary>
public class CaptureResult
{
    public string CaptureId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Gross { get; set; }
    public decimal Fee { get; set; }
    public decimal Net { get; set; }
    public string Currency { get; set; } = string.Empty;
}

/// <summary>Result of refunding a capture.</summary>
public class RefundResult
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

/// <summary>Result of vaulting a card, carrying only safe descriptor fields.</summary>
public class VaultResult
{
    public string VaultId { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? ExpiryMonth { get; set; }
    public string? ExpiryYear { get; set; }
}

/// <summary>Current state of an authorization, used to decide whether it must be renewed.</summary>
public class AuthorizationState
{
    public string AuthorizationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>One transaction from PayPal's own records, for reconciliation.</summary>
public class GatewayTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? InitiationDate { get; set; }
    /// <summary>The invoice id we attach at authorize time (our order id), when PayPal echoes it.</summary>
    public string? InvoiceId { get; set; }
    /// <summary>The custom id we attach (our order id), when PayPal echoes it.</summary>
    public string? CustomId { get; set; }
    public string? EventCode { get; set; }
}
