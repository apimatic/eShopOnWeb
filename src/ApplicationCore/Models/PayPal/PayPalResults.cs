using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

public class PayPalAuthorizationResult
{
    public string AuthorizationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
}

public class PayPalCaptureResult
{
    public string CaptureId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
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

public class PayPalVaultedCardResult
{
    public string VaultTokenId { get; set; } = string.Empty;
    public string? CustomerId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
}

public class PayPalTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public string? Subject { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public DateTimeOffset? InitiatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? ReferenceId { get; set; }
    public string? CustomField { get; set; }
}
