using System;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Full card details, accepted over TLS and forwarded to PayPal only.
/// Never persisted, never logged. Expiry accepts YYYY-MM, MM/YYYY or MM/YY.
/// </summary>
public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }

    public GatewayCardDetails ToGatewayCardDetails()
    {
        return new GatewayCardDetails
        {
            Number = (Number ?? string.Empty).Replace(" ", string.Empty),
            Expiry = NormalizeExpiry(Expiry),
            SecurityCode = SecurityCode ?? string.Empty,
            CardholderName = CardholderName ?? string.Empty,
            BillingAddressLine1 = BillingAddressLine1,
            BillingAddressLine2 = BillingAddressLine2,
            BillingCity = BillingCity,
            BillingState = BillingState,
            BillingPostalCode = BillingPostalCode,
            BillingCountryCode = BillingCountryCode
        };
    }

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(Number)
            && !string.IsNullOrWhiteSpace(SecurityCode)
            && !string.IsNullOrWhiteSpace(CardholderName)
            && TryNormalizeExpiry(Expiry, out _);
    }

    /// <summary>Normalizes YYYY-MM, MM/YYYY, MM-YYYY and MM/YY to PayPal's YYYY-MM.</summary>
    public static string NormalizeExpiry(string expiry)
    {
        if (!TryNormalizeExpiry(expiry, out var normalized))
        {
            throw new ArgumentException("Expiry must be YYYY-MM, MM/YYYY or MM/YY.");
        }
        return normalized;
    }

    private static bool TryNormalizeExpiry(string? expiry, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(expiry))
        {
            return false;
        }

        var parts = expiry.Trim().Split(new[] { '/', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        string year;
        string month;
        if (parts[0].Length == 4)
        {
            year = parts[0];
            month = parts[1];
        }
        else
        {
            month = parts[0];
            year = parts[1].Length == 2 ? $"20{parts[1]}" : parts[1];
        }

        if (year.Length != 4 || month.Length > 2
            || !int.TryParse(year, out _)
            || !int.TryParse(month, out var monthNumber)
            || monthNumber < 1 || monthNumber > 12)
        {
            return false;
        }

        normalized = $"{year}-{monthNumber:D2}";
        return true;
    }
}
