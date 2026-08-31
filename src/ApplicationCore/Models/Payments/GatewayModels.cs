using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>
/// Full card details, held only in memory for the duration of a single PayPal call.
/// Never persisted and never logged.
/// </summary>
public class GatewayCardDetails
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public GatewayAddress? BillingAddress { get; set; }
}

public class GatewayAddress
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;
}

/// <summary>
/// The payment source for an authorization: either full card details for a one-off
/// payment, or the PayPal vault token id of a saved card.
/// </summary>
public class GatewayPaymentSource
{
    public GatewayCardDetails? Card { get; set; }
    public string? VaultTokenId { get; set; }

    public static GatewayPaymentSource FromCard(GatewayCardDetails card) => new() { Card = card };
    public static GatewayPaymentSource FromVaultToken(string vaultTokenId) => new() { VaultTokenId = vaultTokenId };
}

public class PayPalOrderCreated
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class PayPalAuthorizationInfo
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? ExpirationTime { get; set; }
}

public class PayPalCaptureInfo
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class PayPalRefundInfo
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class PayPalVaultedCard
{
    public string PaymentTokenId { get; set; } = string.Empty;
    public string? CustomerId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class PayPalTransaction
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

public class PayPalTransactionPage
{
    public List<PayPalTransaction> Transactions { get; set; } = new List<PayPalTransaction>();
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
}
