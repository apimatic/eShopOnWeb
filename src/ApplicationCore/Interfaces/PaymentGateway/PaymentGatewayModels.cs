using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;

/// <summary>
/// Card details for a single payment or for saving. Deliberately has no persistence-friendly shape:
/// it is handed to the gateway and dropped. <see cref="ToString"/> is redacted so it can never leak
/// into a log.
/// </summary>
public class CardDetails
{
    public required string Number { get; init; }
    public required string Expiry { get; init; }
    public required string SecurityCode { get; init; }
    public required string CardHolderName { get; init; }
    public string? Street { get; init; }
    public string? City { get; init; }
    public string? Region { get; init; }
    public string? PostalCode { get; init; }
    public string? CountryCode { get; init; }

    public override string ToString() => "CardDetails([redacted])";
}

/// <summary>A reference to a card the shopper has already saved with the processor.</summary>
public class SavedCardReference
{
    public required string VaultId { get; init; }
    public required string PayPalCustomerId { get; init; }
}

public class AuthorizePaymentRequest
{
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string InvoiceId { get; init; }

    /// <summary>Carried on the transaction so PayPal's statement can be tied back to an eShop order.</summary>
    public string? CustomId { get; init; }

    public string? Description { get; init; }

    /// <summary>Replayed to the processor so a retried request cannot create a second hold.</summary>
    public required string RequestId { get; init; }

    public CardDetails? Card { get; init; }
    public SavedCardReference? SavedCard { get; init; }
}

public class PaymentAuthorization
{
    public required string PayPalOrderId { get; init; }
    public required string AuthorizationId { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
    public decimal Amount { get; init; }
    public string? Currency { get; init; }
    public string? DeclineCode { get; init; }
    public bool IsCapturable => Status is "CREATED" or "PENDING" or "PARTIALLY_CAPTURED";
    public bool IsStale => Status is "VOIDED" or "EXPIRED" or "DENIED" or "CAPTURED"
        || (ExpirationTime.HasValue && ExpirationTime.Value <= DateTimeOffset.UtcNow);
}

public class CapturedPayment
{
    public required string CaptureId { get; init; }
    public required string Status { get; init; }
    public decimal GrossAmount { get; init; }
    public decimal FeeAmount { get; init; }
    public decimal NetAmount { get; init; }
    public string? Currency { get; init; }
}

public class RefundedPayment
{
    public required string RefundId { get; init; }
    public required string Status { get; init; }
    public decimal Amount { get; init; }
    public decimal? FeeReturned { get; init; }
    public decimal? NetAmount { get; init; }
    public decimal TotalRefunded { get; init; }
    public string? Currency { get; init; }
}

/// <summary>What the processor knows about a saved card. Never contains a card number.</summary>
public class SavedCardToken
{
    public required string VaultId { get; init; }
    public required string PayPalCustomerId { get; init; }
    public string? Brand { get; init; }
    public string? Last4 { get; init; }
    public string? Expiry { get; init; }
    public string? CardHolderName { get; init; }
    public string? BillingCountry { get; init; }
}

/// <summary>One line of the processor's own account statement for a date range.</summary>
public class ProcessorTransactionLine
{
    public required string TransactionId { get; init; }
    public string? ReferenceId { get; init; }
    public string? ReferenceIdType { get; init; }
    public string? EventCode { get; init; }
    public string? Status { get; init; }
    public decimal Amount { get; init; }
    public string? Currency { get; init; }
    public decimal? FeeAmount { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public DateTimeOffset TransactionDate { get; init; }
}
