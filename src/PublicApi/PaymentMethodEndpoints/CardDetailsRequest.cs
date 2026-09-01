using System;
using System.Globalization;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Raw card details, used only to authorize or vault with PayPal. Never persisted, never logged.
/// </summary>
public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public string? BillingCountryCode { get; set; }

    /// <summary>Returns null when valid, otherwise a caller-safe validation message.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Number) || !Number.All(char.IsDigit) || Number.Length < 12 || Number.Length > 19)
        {
            return "card.number must be 12-19 digits.";
        }
        if (ExpiryMonth < 1 || ExpiryMonth > 12)
        {
            return "card.expiryMonth must be between 1 and 12.";
        }
        if (ExpiryYear < 2000 || ExpiryYear > 2200)
        {
            return "card.expiryYear must be a 4-digit year.";
        }
        if (BillingCountryCode is not null && BillingCountryCode.Length != 2)
        {
            return "card.billingCountryCode must be a 2-letter ISO country code.";
        }
        return null;
    }

    public CardPaymentDetails ToModel() => new(
        Number,
        string.Create(CultureInfo.InvariantCulture, $"{ExpiryYear:D4}-{ExpiryMonth:D2}"),
        SecurityCode,
        Name,
        BillingCountryCode ?? "US");
}
