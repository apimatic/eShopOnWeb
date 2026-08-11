using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>A monetary amount as PayPal models it: a currency code and a value.</summary>
public record Money(string CurrencyCode, decimal Value);

/// <summary>
/// Raw card details for a one-off payment or for vaulting. These are passed straight through to
/// PayPal and are never persisted in this app's database nor written to logs.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? CardholderName,
    CardBillingAddress? BillingAddress);

/// <summary>The portable postal address shape PayPal's card schemas accept.</summary>
public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string? CountryCode);

/// <summary>
/// The instrument a shopper pays with: exactly one of a raw <see cref="Card"/> for a one-off
/// payment, or a <see cref="VaultId"/> naming a saved card.
/// </summary>
public record PaymentInstrument(CardDetails? Card, string? VaultId)
{
    public bool IsSavedCard => !string.IsNullOrEmpty(VaultId);
}

/// <summary>A request to place a hold (authorize) on a card for an order amount.</summary>
public record AuthorizeCommand(
    Money Amount,
    string InvoiceId,
    string? CustomId,
    string? Description,
    PaymentInstrument Instrument,
    string IdempotencyKey);

/// <summary>The state PayPal owns for a hold after an authorization or re-authorization.</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>What PayPal reported for a capture: the captured amount, its fee, and the net proceeds.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    Money GrossAmount,
    Money? PayPalFee,
    Money? NetAmount);

public record RefundResult(string RefundId, string Status, Money Amount);

/// <summary>The safe descriptor PayPal returns for a vaulted card, plus the token used to pay with it.</summary>
public record VaultedCardResult(
    string VaultId,
    string PayPalCustomerId,
    string Brand,
    string LastDigits,
    string? CardholderName,
    string? Expiry);

/// <summary>One transaction row from PayPal's own reporting, for reconciliation.</summary>
public record ReconciliationTransaction(
    string TransactionId,
    string? Status,
    string? EventCode,
    DateTimeOffset? InitiationDate,
    decimal? Amount,
    string? CurrencyCode,
    string? FeeAmount,
    string? InvoiceId,
    string? CustomId,
    string? ReferenceId,
    string? PaymentMethodType);
