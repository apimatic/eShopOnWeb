using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>An amount of money in a given currency.</summary>
public record Money(decimal Amount, string Currency);

/// <summary>
/// Raw card details for a one-off payment or for vaulting. Never persisted in this app's database
/// and never logged.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry, // "YYYY-MM"
    string SecurityCode,
    string? Name,
    BillingAddress? BillingAddress);

/// <summary>Billing address for a card (all optional; sandbox accepts any).</summary>
public record BillingAddress(
    string? AddressLine1,
    string? AdminArea2, // city
    string? AdminArea1, // state
    string? PostalCode,
    string? CountryCode);

/// <summary>Result of authorizing (holding) funds against a PayPal checkout order.</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of capturing an authorization, carrying the amounts PayPal reported.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount);

/// <summary>Result of refunding a capture.</summary>
public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount);

/// <summary>Result of vaulting a card, with a safe description of it (never full details).</summary>
public record VaultResult(
    string VaultTokenId,
    string? Brand,
    string? Last4,
    string? Expiry,
    string? CardHolderName);

/// <summary>One transaction as PayPal's own records report it, for reconciliation.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? Date,
    string? InvoiceId,
    string? CustomField);

/// <summary>A card payment challenge that would require a browser approval round-trip.</summary>
public class PaymentApprovalRequiredException : Exception
{
    public PaymentApprovalRequiredException(string message) : base(message) { }
}

/// <summary>A PayPal call failed; carries the PayPal error payload for surfacing to an operator.</summary>
public class PayPalApiException : Exception
{
    public int StatusCode { get; }
    public string? PayPalName { get; }
    public string? RawBody { get; }

    public PayPalApiException(string message, int statusCode, string? payPalName, string? rawBody)
        : base(message)
    {
        StatusCode = statusCode;
        PayPalName = payPalName;
        RawBody = rawBody;
    }
}

/// <summary>
/// An authorization that has expired beyond what PayPal will let us reauthorize — the hold can no
/// longer be renewed, so fulfilment cannot proceed. Phrased for an operator to act on.
/// </summary>
public class AuthorizationNotRenewableException : Exception
{
    public AuthorizationNotRenewableException(string message) : base(message) { }
}
