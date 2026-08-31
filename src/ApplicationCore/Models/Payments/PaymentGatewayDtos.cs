using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>
/// Full card details, used only in transit between the API endpoint and the
/// payment provider. Never persisted, never logged.
/// </summary>
public class CardDetails
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // YYYY-MM
    public string? SecurityCode { get; set; }
    public string? HolderName { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AdminArea2 { get; set; } // city
    public string? AdminArea1 { get; set; } // state/province
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

/// <summary>Either a one-off card or a reference to a vaulted card.</summary>
public class PaymentSourceDto
{
    public CardDetails? Card { get; set; }
    public string? VaultTokenId { get; set; }

    public static PaymentSourceDto ForCard(CardDetails card) => new() { Card = card };
    public static PaymentSourceDto ForVaultedCard(string vaultTokenId) => new() { VaultTokenId = vaultTokenId };
}

public class AuthorizationResult
{
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? ExpirationTime { get; set; }
}

public class AuthorizationDetails
{
    public string AuthorizationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? ExpirationTime { get; set; }
}

public class CaptureResult
{
    public string CaptureId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class RefundResult
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal? TotalRefundedAmount { get; set; }
}

public class VaultedCardResult
{
    public string VaultTokenId { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
}

public class ProviderTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? ReferenceIdType { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public DateTimeOffset? UpdatedDate { get; set; }
}
