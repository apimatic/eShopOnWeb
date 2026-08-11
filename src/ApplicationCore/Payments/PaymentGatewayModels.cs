using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>An amount in a currency. Value is the decimal amount; formatting to PayPal's string form is the gateway's job.</summary>
public record PayPalMoney(decimal Value, string CurrencyCode);

/// <summary>
/// Raw card details supplied for a one-off payment or to vault a card. These are passed straight to PayPal and
/// are never persisted in the application's database or written to logs.
/// </summary>
public record CardDetails
{
    public required string Number { get; init; }
    /// <summary>Expiry in PayPal's YYYY-MM form.</summary>
    public required string ExpiryYearMonth { get; init; }
    public string? SecurityCode { get; init; }
    public string? CardholderName { get; init; }

    // Billing address. Country code is required by the PayPal card schema.
    public required string BillingCountryCode { get; init; }
    public string? BillingAddressLine1 { get; init; }
    public string? BillingAddressLine2 { get; init; }
    public string? BillingAdminArea1 { get; init; } // state / province
    public string? BillingAdminArea2 { get; init; } // city
    public string? BillingPostalCode { get; init; }
}

/// <summary>Request to authorize (place a hold for) an order total via a card or a vaulted card.</summary>
public record AuthorizeOrderRequest
{
    public required PayPalMoney Amount { get; init; }
    public required string InvoiceId { get; init; }
    public required string CustomId { get; init; }
    /// <summary>Idempotency key sent as PayPal-Request-Id so a retry never places a second hold.</summary>
    public required string RequestId { get; init; }
    /// <summary>Raw card for a one-off payment. Mutually exclusive with <see cref="VaultId"/>.</summary>
    public CardDetails? Card { get; init; }
    /// <summary>A saved card's PayPal vault id. Mutually exclusive with <see cref="Card"/>.</summary>
    public string? VaultId { get; init; }
    public string? SoftDescriptor { get; init; }
}

/// <summary>The hold PayPal placed, as returned in purchase_units[].payments.authorizations[].</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    PayPalMoney Amount,
    DateTimeOffset? ExpiresAt);

/// <summary>The captured payment, including PayPal's fee and the net proceeds to the merchant.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    PayPalMoney Gross,
    PayPalMoney? PayPalFee,
    PayPalMoney? NetAmount);

/// <summary>A refund against a capture, with the running total refunded to date.</summary>
public record RefundResult(
    string RefundId,
    string Status,
    PayPalMoney Amount,
    PayPalMoney? TotalRefunded);

/// <summary>Request to vault (save) a card for a customer.</summary>
public record VaultCardRequest(CardDetails Card, string CustomerId);

/// <summary>The vaulted card: its token id plus a safe descriptor (never the full card details).</summary>
public record VaultedCardResult(
    string TokenId,
    string CustomerId,
    string Brand,
    string LastDigits,
    string ExpiryYearMonth,
    string? CardholderName);

/// <summary>A single page request against the PayPal transaction-search reporting API.</summary>
public record TransactionSearchQuery(DateTimeOffset StartDate, DateTimeOffset EndDate, int Page, int PageSize);

/// <summary>One page of transaction-search results plus the paging metadata needed to fetch the rest.</summary>
public record TransactionSearchPage(
    IReadOnlyList<PayPalTransaction> Transactions,
    int Page,
    int TotalPages,
    int TotalItems);

/// <summary>PayPal's own record of a transaction, from the reporting API.</summary>
public record PayPalTransaction
{
    public required string TransactionId { get; init; }
    public string? EventCode { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
    public DateTimeOffset? UpdatedDate { get; init; }
    public PayPalMoney? Amount { get; init; }
    public PayPalMoney? FeeAmount { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public string? ReferenceId { get; init; }
}
