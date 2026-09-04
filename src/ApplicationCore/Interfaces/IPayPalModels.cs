using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>An amount of money in a specific currency.</summary>
public record PayPalMoney(string CurrencyCode, decimal Value)
{
    public string Formatted => Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Billing address for a card payment.</summary>
public record PayPalCardAddress(string Line1, string Line2, string City, string State, string PostalCode, string CountryCode);

/// <summary>
/// Raw card details for a one-off payment. This type must never be logged
/// and must never be persisted - it exists only in-flight.
/// </summary>
public record PayPalCardPayment(
    string Number,
    string Expiry,
    string Name,
    PayPalCardAddress BillingAddress);

/// <summary>Authorization (hold) as reported by PayPal.</summary>
public record PayPalAuthorizationInfo(
    string Id,
    string Status,
    PayPalMoney Amount,
    DateTimeOffset? ExpirationTime);

/// <summary>Capture as reported by PayPal, including fee breakdown.</summary>
public record PayPalCaptureInfo(
    string Id,
    string Status,
    PayPalMoney Amount,
    PayPalMoney? PayPalFee,
    PayPalMoney? NetAmount);

/// <summary>Refund as reported by PayPal.</summary>
public record PayPalRefundInfo(
    string Id,
    string Status,
    PayPalMoney Amount);

/// <summary>A vaulted payment method (saved card) as reported by PayPal.</summary>
public record PayPalPaymentTokenInfo(
    string Id,
    string? CustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? Name);

/// <summary>One transaction as recorded by PayPal's transaction report.</summary>
public record PayPalTransactionInfo(
    string TransactionId,
    string TransactionType,
    string TransactionStatus,
    DateTimeOffset TransactionInitiationDate,
    PayPalMoney? Amount,
    PayPalMoney? FeeAmount,
    PayPalMoney? NetAmount,
    string? InvoiceId,
    string? CustomId,
    string? ReferenceId);

/// <summary>A full page of PayPal transactions.</summary>
public record PayPalTransactionPage(
    System.Collections.Generic.IReadOnlyList<PayPalTransactionInfo> Transactions,
    int Page,
    int TotalPages);
