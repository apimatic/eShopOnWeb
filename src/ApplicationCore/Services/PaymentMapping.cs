using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>Helpers shared by the payment services: view mapping, amount rounding and expiry normalization.</summary>
public static class PaymentMapping
{
    public static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static PaymentView ToView(OrderPayment p) => new(
        OrderId: p.OrderId,
        Status: p.Status.ToString(),
        Currency: p.CurrencyCode,
        Amount: p.Amount,
        PaymentReference: p.PaymentReference,
        PayPalOrderId: p.PayPalOrderId,
        AuthorizationId: p.AuthorizationId,
        AuthorizationStatus: p.AuthorizationStatus,
        AuthorizationExpiresAt: p.AuthorizationExpiresAt,
        CaptureId: p.CaptureId,
        CaptureStatus: p.CaptureStatus,
        CapturedAmount: p.CapturedAmount,
        PayPalFee: p.PayPalFee,
        NetAmount: p.NetAmount,
        TotalRefunded: p.TotalRefunded(),
        RefundableRemaining: p.RefundableRemaining(),
        PaymentSourceDescription: p.PaymentSourceDescription,
        Refunds: p.Refunds
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RefundView(r.PayPalRefundId, r.Amount, r.Status, r.CreatedAt, r.Reason))
            .ToList(),
        CreatedAt: p.CreatedAt,
        AuthorizedAt: p.AuthorizedAt,
        FulfilledAt: p.FulfilledAt,
        CanceledAt: p.CanceledAt);

    /// <summary>
    /// Normalizes a card expiry to PayPal's "YYYY-MM" form. Accepts "YYYY-MM", "MM/YY" and "MM/YYYY".
    /// Returns null when the input can't be understood.
    /// </summary>
    public static string? NormalizeExpiry(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim();

        // Already "YYYY-MM"
        var iso = Regex.Match(value, @"^(\d{4})-(\d{1,2})$");
        if (iso.Success)
        {
            var year = int.Parse(iso.Groups[1].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(iso.Groups[2].Value, CultureInfo.InvariantCulture);
            return FormatExpiry(year, month);
        }

        // "MM/YY" or "MM/YYYY" (also tolerates '-' separator)
        var slash = Regex.Match(value, @"^(\d{1,2})[/\-](\d{2}|\d{4})$");
        if (slash.Success)
        {
            var month = int.Parse(slash.Groups[1].Value, CultureInfo.InvariantCulture);
            var yearPart = slash.Groups[2].Value;
            var year = yearPart.Length == 2
                ? 2000 + int.Parse(yearPart, CultureInfo.InvariantCulture)
                : int.Parse(yearPart, CultureInfo.InvariantCulture);
            return FormatExpiry(year, month);
        }

        return null;
    }

    private static string? FormatExpiry(int year, int month)
    {
        if (month < 1 || month > 12 || year < 2000 || year > 2100)
        {
            return null;
        }

        return $"{year:D4}-{month:D2}";
    }
}
