using System;
using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.Shared;

public static class CardPaymentMapper
{
    public static PayPalCardDetails ToPayPalCardDetails(CardPaymentRequest request)
    {
        return new PayPalCardDetails(
            request.Name,
            request.Number,
            NormalizeExpiry(request.Expiry),
            request.SecurityCode,
            request.BillingAddress is null
                ? null
                : new PayPalCardAddress(
                    request.BillingAddress.AddressLine1,
                    request.BillingAddress.AddressLine2,
                    request.BillingAddress.AdminArea2,
                    request.BillingAddress.AdminArea1,
                    request.BillingAddress.PostalCode,
                    request.BillingAddress.CountryCode));
    }

    /// <summary>
    /// Normalizes "MM/YYYY" or "YYYY-MM" into the "YYYY-MM" wire format PayPal expects.
    /// </summary>
    public static string NormalizeExpiry(string expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry))
        {
            return string.Empty;
        }

        var trimmed = expiry.Trim();

        if (trimmed.Contains('/'))
        {
            var parts = trimmed.Split('/');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var month) &&
                int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var year))
            {
                return $"{year:D4}-{month:D2}";
            }
        }
        else if (trimmed.Contains('-') && trimmed.Length == 7)
        {
            return trimmed;
        }

        return trimmed;
    }
}