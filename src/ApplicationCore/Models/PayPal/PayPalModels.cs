using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

/// <summary>Card details used for a one-off payment or for vaulting. Never persisted or logged.</summary>
public record PayPalCard(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    PayPalAddress? BillingAddress);

public record PayPalAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

public record PayPalAuthorization(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpiresAt);

public record PayPalCapture(
    string Id,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

public record PayPalRefundResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency);

public record PayPalVaultToken(
    string Id,
    string? Brand,
    string? LastDigits,
    string? Expiry);

public record PayPalTransaction(
    string TransactionId,
    string? ReferenceId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    DateTimeOffset? InitiatedAt,
    string? InvoiceId,
    string? CustomField);
